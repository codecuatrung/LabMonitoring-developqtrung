import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AgentPackage, AgentUpdatesService } from '../../../core/api/agent-updates.service';
import { PageTitleService } from '../../../core/layout/page-title.service';

/** Upload/history for LabAgent auto-update packages (SRS H1). Pushing a package to a machine/room
 * happens from the command panel (Computer/Room Detail) — this page only manages what's available to
 * push, matching the "most recently uploaded = current" rule the backend enforces. */
@Component({
  selector: 'app-agent-updates-list',
  imports: [FormsModule, DatePipe],
  templateUrl: './agent-updates-list.html',
})
export class AgentUpdatesListComponent implements OnInit {
  private readonly api = inject(AgentUpdatesService);
  private readonly pageTitle = inject(PageTitleService);

  readonly packages = signal<AgentPackage[]>([]);
  readonly error = signal<string | null>(null);
  readonly uploading = signal(false);
  readonly showUploadModal = signal(false);

  version = '';
  file: File | null = null;

  ngOnInit(): void {
    this.pageTitle.set('Phiên bản Agent');
    this.load();
  }

  load(): void {
    this.api.list().subscribe({
      next: (p) => this.packages.set(p),
      error: (e) => this.fail(e),
    });
  }

  openUploadModal(): void {
    this.version = '';
    this.file = null;
    this.error.set(null);
    this.showUploadModal.set(true);
  }

  closeUploadModal(): void {
    this.showUploadModal.set(false);
  }

  onFileSelected(ev: Event): void {
    this.file = (ev.target as HTMLInputElement).files?.[0] ?? null;
  }

  upload(): void {
    if (!this.version.trim() || !this.file) return;
    this.uploading.set(true);
    this.error.set(null);
    this.api.upload(this.version.trim(), this.file).subscribe({
      next: () => {
        this.uploading.set(false);
        this.showUploadModal.set(false);
        this.load();
      },
      error: (e) => {
        this.uploading.set(false);
        this.fail(e);
      },
    });
  }

  formatSize(bytes: number): string {
    const mb = bytes / (1024 * 1024);
    return `${mb.toFixed(1)} MB`;
  }

  private fail(e: unknown): void {
    const err = e as { error?: { detail?: string }; message?: string };
    this.error.set(err?.error?.detail ?? err?.message ?? 'Thao tác thất bại');
  }
}
