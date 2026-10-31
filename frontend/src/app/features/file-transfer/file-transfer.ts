import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FacilitiesService } from '../../core/api/facilities.service';
import {
  FileTransferDetail,
  FileTransferSummary,
  FileTransfersService,
  TransferDirection,
  TransferFolder,
} from '../../core/api/file-transfers.service';
import { PageTitleService } from '../../core/layout/page-title.service';
import { Room, RoomComputer } from '../../core/models/facilities.models';
import { commandStatusClass, commandStatusLabel } from '../../core/models/status-ui';

type Tab = 'push' | 'collect' | 'folders' | 'history';

const MAX_PUSH_BYTES = 500 * 1024 * 1024; // mirrors FileTransfer:MaxPushBytes (the server enforces the real limit)
const BLOCKED_EXTENSIONS = ['.exe', '.dll', '.bat', '.cmd', '.ps1', '.msi', '.scr', '.vbs', '.js', '.com', '.jar', '.lnk'];
const POLL_MS = 3000;
const POLL_MAX_MS = 20 * 60 * 1000;

/**
 * "Truyền file": push a file from the server to one machine / a whole room, or collect a folder back from the machines.
 * Only folders on the admin-managed allow-list can be targeted (the server and each Agent re-check that list).
 * Progress is polled (every few seconds) from GET /file-transfers/{id}, which merges the per-machine command results.
 */
@Component({
  selector: 'app-file-transfer',
  imports: [FormsModule, DatePipe],
  templateUrl: './file-transfer.html',
})
export class FileTransferComponent implements OnInit, OnDestroy {
  private readonly api = inject(FileTransfersService);
  private readonly facilities = inject(FacilitiesService);
  private readonly pageTitle = inject(PageTitleService);

  readonly commandStatusClass = commandStatusClass;
  readonly commandStatusLabel = commandStatusLabel;

  readonly tab = signal<Tab>('push');
  readonly error = signal<string | null>(null);
  readonly rooms = signal<Room[]>([]);
  readonly roomComputers = signal<RoomComputer[]>([]);

  // Target selection (shared by push/collect)
  targetKind: 'room' | 'computer' = 'room';
  roomId: number | null = null;
  computerId: number | null = null;

  readonly folders = signal<TransferFolder[]>([]);
  readonly allFolders = signal<TransferFolder[]>([]);
  readonly history = signal<FileTransferSummary[]>([]);

  // Push
  pushFolderId: number | null = null;
  overwrite = false;
  file: File | null = null;
  readonly uploadPercent = signal<number | null>(null);

  // Collect
  collectFolderId: number | null = null;
  filterText = '*.docx;*.pdf';

  // Tracking
  readonly active = signal<FileTransferDetail | null>(null);
  readonly busy = signal(false);
  private pollHandle: ReturnType<typeof setInterval> | null = null;
  private pollStartedAt = 0;

  // Confirm modal (room-wide operations)
  readonly confirmKind = signal<'push' | 'collect' | null>(null);

  // Folder editor modal
  readonly folderEditor = signal<{ id: number | null } | null>(null);
  folderForm = { roomId: null as number | null, path: '', direction: 'Push' as TransferDirection, label: '', isActive: true };
  readonly folderToDelete = signal<TransferFolder | null>(null);

  // Plain method (not computed): targetKind is a plain ngModel field, which computed() would never re-evaluate for.
  isRoomWide(): boolean {
    return this.targetKind === 'room';
  }

  ngOnInit(): void {
    this.pageTitle.set('Truyền file');
    this.facilities.rooms({ pageSize: 200 }).subscribe({
      next: (r) => {
        this.rooms.set(r.items);
        if (r.items.length > 0) {
          this.roomId = r.items[0].id;
          this.onTargetChanged();
        }
      },
      error: (e) => this.fail(e),
    });
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  setTab(tab: Tab): void {
    this.tab.set(tab);
    this.error.set(null);
    if (tab === 'folders') this.loadAllFolders();
    if (tab === 'history') this.loadHistory();
  }

  // ---- target / folders ----

  onTargetChanged(): void {
    if (this.roomId == null) return;
    this.pushFolderId = null;
    this.collectFolderId = null;

    this.api.folders({ roomId: this.roomId }).subscribe({
      next: (f) => {
        this.folders.set(f);
        this.pushFolderId = f.find((x) => x.direction === 'Push')?.id ?? null;
        this.collectFolderId = f.find((x) => x.direction === 'Collect')?.id ?? null;
      },
      error: (e) => this.fail(e),
    });

    this.facilities.computers({ roomId: this.roomId }).subscribe({
      next: (c) => {
        const usable = c.filter((x) => x.isApproved && x.isActive);
        this.roomComputers.set(usable);
        if (!usable.some((x) => x.id === this.computerId)) this.computerId = usable[0]?.id ?? null;
      },
      error: (e) => this.fail(e),
    });
  }

  pushFolders(): TransferFolder[] {
    return this.folders().filter((f) => f.direction === 'Push');
  }

  collectFolders(): TransferFolder[] {
    return this.folders().filter((f) => f.direction === 'Collect');
  }

  targetLabel(): string {
    if (this.targetKind === 'room') {
      const room = this.rooms().find((r) => r.id === this.roomId);
      return room ? `cả phòng ${room.name}` : 'cả phòng';
    }
    return this.roomComputers().find((c) => c.id === this.computerId)?.name ?? 'máy đã chọn';
  }

  // ---- push ----

  onFileSelected(ev: Event): void {
    const file = (ev.target as HTMLInputElement).files?.[0] ?? null;
    this.file = file;
    this.error.set(null);
    if (!file) return;

    const ext = file.name.includes('.') ? '.' + file.name.split('.').pop()!.toLowerCase() : '';
    if (BLOCKED_EXTENSIONS.includes(ext)) {
      this.error.set(`Không cho phép gửi file loại "${ext}" (file thực thi/script).`);
      this.file = null;
    } else if (file.size > MAX_PUSH_BYTES) {
      this.error.set(`File quá lớn (tối đa ${MAX_PUSH_BYTES / (1024 * 1024)} MB).`);
      this.file = null;
    }
  }

  canPush(): boolean {
    const hasTarget = this.targetKind === 'room' ? this.roomId != null : this.computerId != null;
    return !this.busy() && !!this.file && this.pushFolderId != null && hasTarget;
  }

  startPush(): void {
    if (!this.canPush()) return;
    if (this.isRoomWide()) {
      this.confirmKind.set('push');
      return;
    }
    this.doPush(false);
  }

  private doPush(confirm: boolean): void {
    if (!this.file || this.pushFolderId == null) return;
    this.error.set(null);
    this.busy.set(true);
    this.uploadPercent.set(0);
    this.active.set(null);

    this.api
      .push({
        file: this.file,
        folderId: this.pushFolderId,
        overwrite: this.overwrite,
        confirm,
        roomId: this.targetKind === 'room' ? this.roomId! : undefined,
        computerId: this.targetKind === 'computer' ? this.computerId! : undefined,
      })
      .subscribe({
        next: (p) => {
          this.uploadPercent.set(p.percent);
          if (p.result) {
            this.uploadPercent.set(null);
            this.busy.set(false);
            this.track(p.result.transferId);
          }
        },
        error: (e) => {
          this.uploadPercent.set(null);
          this.busy.set(false);
          this.fail(e);
        },
      });
  }

  // ---- collect ----

  canCollect(): boolean {
    const hasTarget = this.targetKind === 'room' ? this.roomId != null : this.computerId != null;
    return !this.busy() && this.collectFolderId != null && hasTarget;
  }

  startCollect(): void {
    if (!this.canCollect()) return;
    if (this.isRoomWide()) {
      this.confirmKind.set('collect');
      return;
    }
    this.doCollect(false);
  }

  private doCollect(confirm: boolean): void {
    if (this.collectFolderId == null) return;
    this.error.set(null);
    this.busy.set(true);
    this.active.set(null);

    this.api
      .collect({
        folderId: this.collectFolderId,
        filter: this.filterText.trim() || null,
        confirm,
        roomId: this.targetKind === 'room' ? this.roomId! : undefined,
        computerId: this.targetKind === 'computer' ? this.computerId! : undefined,
      })
      .subscribe({
        next: (r) => {
          this.busy.set(false);
          this.track(r.transferId);
        },
        error: (e) => {
          this.busy.set(false);
          this.fail(e);
        },
      });
  }

  confirmRoomWide(): void {
    const kind = this.confirmKind();
    this.confirmKind.set(null);
    if (kind === 'push') this.doPush(true);
    if (kind === 'collect') this.doCollect(true);
  }

  cancelConfirm(): void {
    this.confirmKind.set(null);
  }

  // ---- tracking ----

  track(transferId: number): void {
    this.stopPolling();
    this.pollStartedAt = Date.now();
    const refresh = () =>
      this.api.get(transferId).subscribe({
        next: (d) => {
          this.active.set(d);
          const pending = d.targets.some((t) => t.status === 'Sent' || t.status === 'Acknowledged');
          if (!pending || Date.now() - this.pollStartedAt > POLL_MAX_MS) this.stopPolling();
        },
        error: (e) => {
          this.fail(e);
          this.stopPolling();
        },
      });
    refresh();
    this.pollHandle = setInterval(refresh, POLL_MS);
  }

  private stopPolling(): void {
    if (this.pollHandle) clearInterval(this.pollHandle);
    this.pollHandle = null;
  }

  isRunning(): boolean {
    return this.pollHandle !== null;
  }

  summary(): { done: number; failed: number; pending: number; total: number } {
    const targets = this.active()?.targets ?? [];
    return {
      total: targets.length,
      done: targets.filter((t) => t.status === 'Executed').length,
      failed: targets.filter((t) => t.status === 'Failed' || t.status === 'Timeout').length,
      pending: targets.filter((t) => t.status === 'Sent' || t.status === 'Acknowledged').length,
    };
  }

  download(transferId: number, computerId: number, computerName: string): void {
    this.api.downloadCollected(transferId, computerId).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `thu-file-${transferId}-${computerName}.zip`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: (e) => this.fail(e),
    });
  }

  openHistory(id: number): void {
    this.tab.set(this.history().find((h) => h.id === id)?.direction === 'Collect' ? 'collect' : 'push');
    this.track(id);
  }

  // ---- history ----

  loadHistory(): void {
    this.api.list({}).subscribe({ next: (h) => this.history.set(h), error: (e) => this.fail(e) });
  }

  // ---- allowed folders (admin) ----

  loadAllFolders(): void {
    this.api.folders({ includeInactive: true }).subscribe({ next: (f) => this.allFolders.set(f), error: (e) => this.fail(e) });
  }

  newFolder(): void {
    this.folderForm = { roomId: this.roomId, path: '', direction: 'Push', label: '', isActive: true };
    this.error.set(null);
    this.folderEditor.set({ id: null });
  }

  editFolder(f: TransferFolder): void {
    this.folderForm = { roomId: f.roomId, path: f.path, direction: f.direction, label: f.label, isActive: f.isActive };
    this.error.set(null);
    this.folderEditor.set({ id: f.id });
  }

  closeFolderEditor(): void {
    this.folderEditor.set(null);
  }

  saveFolder(): void {
    const editor = this.folderEditor();
    if (!editor) return;
    const input = { ...this.folderForm, path: this.folderForm.path.trim(), label: this.folderForm.label.trim() };
    const call = editor.id == null ? this.api.createFolder(input) : this.api.updateFolder(editor.id, input);
    call.subscribe({
      next: () => {
        this.folderEditor.set(null);
        this.loadAllFolders();
        this.onTargetChanged();
      },
      error: (e) => this.fail(e),
    });
  }

  askDeleteFolder(f: TransferFolder): void {
    this.folderToDelete.set(f);
  }

  cancelDeleteFolder(): void {
    this.folderToDelete.set(null);
  }

  confirmDeleteFolder(): void {
    const f = this.folderToDelete();
    if (!f) return;
    this.api.deleteFolder(f.id).subscribe({
      next: () => {
        this.folderToDelete.set(null);
        this.loadAllFolders();
        this.onTargetChanged();
      },
      error: (e) => {
        this.folderToDelete.set(null);
        this.fail(e);
      },
    });
  }

  // ---- helpers ----

  formatSize(bytes: number | null | undefined): string {
    if (bytes == null) return '—';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  private fail(e: unknown): void {
    const err = e as { status?: number; error?: { detail?: string; title?: string; errors?: Record<string, string[]> }; message?: string };
    const validation = err?.error?.errors ? Object.values(err.error.errors).flat().join(' ') : null;
    if (err?.status === 403 && !err?.error?.detail) {
      this.error.set('Bạn không có quyền thực hiện thao tác này.');
      return;
    }
    this.error.set(validation ?? err?.error?.detail ?? err?.message ?? 'Thao tác thất bại');
  }
}
