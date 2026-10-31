import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Role, RolesService } from '../../../core/api/roles.service';
import { ScreensService } from '../../../core/api/screens.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import { ScreenSummary, flattenScreensForChecklist } from '../../../core/models/screens.models';
import { PaginationComponent } from '../../shared/pagination/pagination';

@Component({
  selector: 'app-roles-list',
  imports: [FormsModule, PaginationComponent],
  templateUrl: './roles-list.html',
})
export class RolesListComponent implements OnInit {
  private readonly rolesApi = inject(RolesService);
  private readonly screensApi = inject(ScreensService);
  private readonly pageTitle = inject(PageTitleService);

  /** Roles are a small, admin-curated catalog also consumed whole elsewhere (user-detail's role
   * checklist) — the API always returns everything; this page paginates it client-side purely for
   * a consistent browsing UI, not because the dataset can grow unbounded. */
  readonly allRoles = signal<Role[]>([]);
  readonly error = signal<string | null>(null);
  readonly confirmDeleteId = signal(0);
  readonly showCreateModal = signal(false);
  newRoleName = '';

  readonly page = signal(1);
  readonly pageSize = signal(20);

  readonly roles = computed(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.allRoles().slice(start, start + this.pageSize());
  });

  // --- "Manage role" modal ---

  readonly manageRole = signal<Role | null>(null);
  readonly screens = signal<ScreenSummary[]>([]);
  readonly screenRows = computed(() => flattenScreensForChecklist(this.screens()));
  readonly saved = signal(false);

  name = '';
  selectedScreenIds = new Set<number>();

  ngOnInit(): void {
    this.pageTitle.set('Vai trò');
    this.load();
    this.screensApi.list().subscribe({ next: (s) => this.screens.set(s) });
  }

  onPageChange(page: number): void {
    this.page.set(page);
  }

  onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.page.set(1);
  }

  load(): void {
    this.rolesApi.list().subscribe({
      next: (r) => this.allRoles.set(r),
      error: (e) => this.fail(e),
    });
  }

  openCreateModal(): void {
    this.newRoleName = '';
    this.showCreateModal.set(true);
  }

  cancelCreate(): void {
    this.showCreateModal.set(false);
  }

  create(): void {
    if (!this.newRoleName.trim()) return;
    this.rolesApi.create(this.newRoleName.trim()).subscribe({
      next: () => {
        this.showCreateModal.set(false);
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
    this.rolesApi.delete(id).subscribe({
      next: () => {
        this.confirmDeleteId.set(0);
        this.load();
      },
      error: (e) => this.fail(e),
    });
  }

  openManageModal(r: Role): void {
    this.error.set(null);
    this.saved.set(false);
    this.manageRole.set(r);
    this.name = r.name;
    this.selectedScreenIds = new Set(r.screenIds);
  }

  closeManageModal(): void {
    this.manageRole.set(null);
  }

  toggleScreen(id: number): void {
    if (this.selectedScreenIds.has(id)) this.selectedScreenIds.delete(id);
    else this.selectedScreenIds.add(id);
  }

  save(): void {
    this.error.set(null);
    this.saved.set(false);
    const role = this.manageRole();
    if (!role) return;

    const rename$ = role.isSystem ? null : this.rolesApi.update(role.id, this.name);
    const applyScreens = () => {
      this.rolesApi.setScreens(role.id, [...this.selectedScreenIds]).subscribe({
        next: () => {
          this.saved.set(true);
          this.closeManageModal();
          this.load();
        },
        error: (e) => this.fail(e),
      });
    };

    if (rename$) {
      rename$.subscribe({ next: applyScreens, error: (e) => this.fail(e) });
    } else {
      applyScreens();
    }
  }

  private fail(e: unknown): void {
    const err = e as { error?: { detail?: string }; message?: string };
    this.error.set(err?.error?.detail ?? err?.message ?? 'Thao tác thất bại');
  }
}
