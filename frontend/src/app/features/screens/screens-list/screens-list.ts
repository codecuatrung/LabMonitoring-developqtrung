import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ScreenInput, ScreensService } from '../../../core/api/screens.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import { ScreenSummary } from '../../../core/models/screens.models';
import { PaginationComponent } from '../../shared/pagination/pagination';

@Component({
  selector: 'app-screens-list',
  imports: [FormsModule, RouterLink, PaginationComponent],
  templateUrl: './screens-list.html',
})
export class ScreensListComponent implements OnInit {
  private readonly screensApi = inject(ScreensService);
  private readonly pageTitle = inject(PageTitleService);

  readonly screens = signal<ScreenSummary[]>([]);
  readonly error = signal<string | null>(null);
  readonly confirmDeleteId = signal(0);
  readonly editingId = signal(0);
  readonly showCreateModal = signal(false);

  /** Rows in display order: top-level screens, each immediately followed by its children (indented) —
   * so "Hệ thống" and its Người dùng/Vai trò/Màn hình chức năng children stay visually grouped. */
  readonly orderedScreens = computed(() => {
    const all = this.screens();
    const byId = new Map(all.map((s) => [s.id, s]));
    const byParent = new Map<number | null, ScreenSummary[]>();
    for (const s of all) {
      const key = s.parentId;
      (byParent.get(key) ?? byParent.set(key, []).get(key)!).push(s);
    }
    for (const list of byParent.values()) list.sort((a, b) => a.displayOrder - b.displayOrder);

    const result: { screen: ScreenSummary; depth: number; parentName: string | null }[] = [];
    for (const top of byParent.get(null) ?? []) {
      result.push({ screen: top, depth: 0, parentName: null });
      for (const child of byParent.get(top.id) ?? []) {
        result.push({ screen: child, depth: 1, parentName: byId.get(child.parentId ?? -1)?.name ?? null });
      }
    }
    return result;
  });

  /** Only top-level, non-group screens can be picked as a parent — keeps the hierarchy to 2 levels. */
  readonly possibleParents = computed(() => this.screens().filter((s) => s.parentId == null));

  readonly page = signal(1);
  readonly pageSize = signal(20);

  /** Screens are a small, admin-curated catalog also consumed whole elsewhere (role/user permission
   * checklists) — the API always returns everything; this page paginates client-side purely for a
   * consistent browsing UI. Slicing the flattened rows means a group could in principle land on a
   * different page than its children once the catalog grows past a page boundary — a non-issue at
   * today's scale (~11 screens). */
  readonly pagedScreens = computed(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.orderedScreens().slice(start, start + this.pageSize());
  });

  onPageChange(page: number): void {
    this.page.set(page);
  }

  onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.page.set(1);
  }

  newScreen: ScreenInput = { code: '', name: '', route: '', icon: null, displayOrder: 0, parentId: null };
  editDraft: { name: string; route: string; icon: string | null; displayOrder: number; isActive: boolean; parentId: number | null } = {
    name: '',
    route: '',
    icon: null,
    displayOrder: 0,
    isActive: true,
    parentId: null,
  };

  ngOnInit(): void {
    this.pageTitle.set('Màn hình chức năng');
    this.load();
  }

  load(): void {
    this.screensApi.list().subscribe({ next: (s) => this.screens.set(s), error: (e) => this.fail(e) });
  }

  openCreateModal(): void {
    this.newScreen = { code: '', name: '', route: '', icon: null, displayOrder: 0, parentId: null };
    this.showCreateModal.set(true);
  }

  cancelCreate(): void {
    this.showCreateModal.set(false);
  }

  create(): void {
    this.screensApi.create(this.newScreen).subscribe({
      next: () => {
        this.showCreateModal.set(false);
        this.load();
      },
      error: (e) => this.fail(e),
    });
  }

  startEdit(s: ScreenSummary): void {
    this.editingId.set(s.id);
    this.editDraft = {
      name: s.name,
      route: s.route ?? '',
      icon: s.icon,
      displayOrder: s.displayOrder,
      isActive: s.isActive,
      parentId: s.parentId,
    };
  }

  cancelEdit(): void {
    this.editingId.set(0);
  }

  saveEdit(id: number): void {
    this.screensApi.update(id, this.editDraft).subscribe({
      next: () => {
        this.editingId.set(0);
        this.load();
      },
      error: (e) => this.fail(e),
    });
  }

  requestDelete(id: number): void {
    this.confirmDeleteId.set(id);
  }

  cancelDelete(): void {
    this.confirmDeleteId.set(0);
  }

  confirmDelete(id: number): void {
    this.screensApi.delete(id).subscribe({
      next: () => {
        this.confirmDeleteId.set(0);
        this.load();
      },
      error: (e) => this.fail(e),
    });
  }

  private fail(e: unknown): void {
    const err = e as { error?: { detail?: string }; message?: string };
    this.error.set(err?.error?.detail ?? err?.message ?? 'Thao tác thất bại');
  }
}
