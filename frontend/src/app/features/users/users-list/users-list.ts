import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UserDetail, UsersService, UserSummary } from '../../../core/api/users.service';
import { Role, RolesService } from '../../../core/api/roles.service';
import { ScreensService } from '../../../core/api/screens.service';
import { FacilitiesService } from '../../../core/api/facilities.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import { Room } from '../../../core/models/facilities.models';
import { ScreenSummary, flattenScreensForChecklist } from '../../../core/models/screens.models';
import { PaginationComponent } from '../../shared/pagination/pagination';

/** Tri-state per screen: undefined = inherit from role, true = always grant, false = always deny. */
type OverrideState = boolean | undefined;

@Component({
  selector: 'app-users-list',
  imports: [FormsModule, PaginationComponent],
  templateUrl: './users-list.html',
})
export class UsersListComponent implements OnInit {
  private readonly usersApi = inject(UsersService);
  private readonly rolesApi = inject(RolesService);
  private readonly screensApi = inject(ScreensService);
  private readonly facilities = inject(FacilitiesService);
  private readonly pageTitle = inject(PageTitleService);

  readonly users = signal<UserSummary[]>([]);
  readonly totalCount = signal(0);
  readonly error = signal<string | null>(null);

  page = 1;
  pageSize = 20;

  // --- "Manage user" modal ---

  private manageUserId = 0;
  readonly manageUser = signal<UserDetail | null>(null);
  readonly roles = signal<Role[]>([]);
  readonly rooms = signal<Room[]>([]);
  readonly screens = signal<ScreenSummary[]>([]);
  readonly screenRows = computed(() => flattenScreensForChecklist(this.screens()));
  readonly saved = signal(false);

  fullName = '';
  phone = '';
  isActive = true;
  // signal(), not plain fields — computed() below only reacts to signal reads, so mutating a plain
  // Set/Map via checkbox toggles would silently never re-trigger it (see [[angular-signals-gotcha]]).
  readonly selectedRoleIds = signal(new Set<number>());
  readonly selectedRoomIds = signal(new Set<number>());
  readonly screenOverrides = signal(new Map<number, OverrideState>());

  readonly needsRoomScope = computed(() => {
    const ids = this.selectedRoleIds();
    const roleNames = this.roles().filter((r) => ids.has(r.id)).map((r) => r.name);
    return roleNames.includes('Technician') || roleNames.includes('Teacher');
  });

  ngOnInit(): void {
    this.pageTitle.set('Người dùng');
    this.load();
    this.rolesApi.list().subscribe({ next: (r) => this.roles.set(r) });
    this.screensApi.list().subscribe({ next: (s) => this.screens.set(s) });
    this.facilities.rooms({ pageSize: 500 }).subscribe({ next: (r) => this.rooms.set(r.items) });
  }

  onPageChange(page: number): void {
    this.page = page;
    this.load();
  }

  onPageSizeChange(pageSize: number): void {
    this.pageSize = pageSize;
    this.page = 1;
    this.load();
  }

  private load(): void {
    this.usersApi.list({ includeInactive: true, page: this.page, pageSize: this.pageSize }).subscribe({
      next: (r) => {
        this.users.set(r.items);
        this.totalCount.set(r.totalCount);
      },
      error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không tải được danh sách người dùng'),
    });
  }

  openManageModal(u: UserSummary): void {
    this.manageUserId = u.id;
    this.saved.set(false);
    this.error.set(null);
    this.usersApi.get(u.id).subscribe({
      next: (detail) => {
        this.manageUser.set(detail);
        this.fullName = detail.fullName;
        this.phone = detail.phone ?? '';
        this.isActive = detail.isActive;
        this.selectedRoleIds.set(new Set(detail.roleIds));
        this.selectedRoomIds.set(new Set(detail.roomScopeIds));
        this.screenOverrides.set(new Map(detail.screenOverrides.map((o) => [o.screenId, o.isGranted])));
      },
      error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không tải được người dùng'),
    });
  }

  closeManageModal(): void {
    this.manageUser.set(null);
  }

  toggleRole(id: number): void {
    this.selectedRoleIds.update((ids) => {
      const next = new Set(ids);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  toggleRoom(id: number): void {
    this.selectedRoomIds.update((ids) => {
      const next = new Set(ids);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  overrideState(screenId: number): OverrideState {
    return this.screenOverrides().get(screenId);
  }

  setOverrideState(screenId: number, state: OverrideState): void {
    this.screenOverrides.update((overrides) => {
      const next = new Map(overrides);
      if (state === undefined) next.delete(screenId);
      else next.set(screenId, state);
      return next;
    });
  }

  save(): void {
    this.error.set(null);
    this.saved.set(false);
    const userId = this.manageUserId;

    this.usersApi.update(userId, { fullName: this.fullName, phone: this.phone || null, isActive: this.isActive }).subscribe({
      next: () => {
        this.usersApi.setRoles(userId, [...this.selectedRoleIds()]).subscribe({
          next: () => {
            this.usersApi.setRoomScopes(userId, [...this.selectedRoomIds()]).subscribe({
              next: () => {
                const overrides = [...this.screenOverrides().entries()].map(([screenId, isGranted]) => ({
                  screenId,
                  isGranted: isGranted!,
                }));
                this.usersApi.setScreenOverrides(userId, overrides).subscribe({
                  next: () => {
                    this.saved.set(true);
                    this.closeManageModal();
                    this.load();
                  },
                  error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không lưu được ghi đè màn hình'),
                });
              },
              error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không lưu được phạm vi phòng'),
            });
          },
          error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không lưu được vai trò'),
        });
      },
      error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không lưu được thông tin người dùng'),
    });
  }
}
