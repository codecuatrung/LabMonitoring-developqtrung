import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { DatePipe } from '@angular/common';
import { filter } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { PermissionService } from '../../core/auth/permission.service';
import { LabHubService } from '../../core/realtime/lab-hub.service';
import { NotificationsService } from '../../core/api/notifications.service';
import { PageTitleService } from '../../core/layout/page-title.service';
import { AppNotification, NotificationCreatedEvent } from '../../core/models/notifications.models';
import { ScreenSummary } from '../../core/models/screens.models';

interface NavGroup extends ScreenSummary {
  children: ScreenSummary[];
}

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DatePipe],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notificationsApi = inject(NotificationsService);
  readonly permission = inject(PermissionService);
  readonly hub = inject(LabHubService);
  readonly pageTitle = inject(PageTitleService).title;

  readonly sidebarOpen = signal(true);
  readonly displayName = this.auth.displayName;

  readonly notifications = signal<AppNotification[]>([]);
  readonly unreadCount = signal(0);

  /** The sidebar menu IS the screen registry (SRS FR-8) — a screen registered + granted to the
   * current user just shows up, no frontend code change needed beyond the route itself existing.
   * A screen with no route and children present (e.g. "Hệ thống") renders as a collapsible submenu
   * group instead of a direct link. */
  readonly navTree = computed<NavGroup[]>(() => {
    const all = this.permission.screens();
    const children = new Map<number, ScreenSummary[]>();
    for (const s of all) {
      if (s.parentId == null) continue;
      (children.get(s.parentId) ?? children.set(s.parentId, []).get(s.parentId)!).push(s);
    }
    return all
      .filter((s) => s.parentId == null)
      .map((s) => ({ ...s, children: (children.get(s.id) ?? []).sort((a, b) => a.displayOrder - b.displayOrder) }))
      .sort((a, b) => a.displayOrder - b.displayOrder);
  });

  private readonly manuallyToggled = signal(new Map<number, boolean>());
  private currentUrl = signal(this.router.url);

  isGroupExpanded(group: NavGroup): boolean {
    const manual = this.manuallyToggled().get(group.id);
    if (manual !== undefined) return manual;
    return group.children.some((c) => c.route && this.currentUrl().startsWith(c.route));
  }

  toggleGroup(id: number): void {
    this.manuallyToggled.update((map) => {
      const next = new Map(map);
      next.set(id, !(next.get(id) ?? false));
      return next;
    });
  }

  async ngOnInit(): Promise<void> {
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => {
      this.currentUrl.set(this.router.url);
    });

    // Already awaited by screenGuard for the very first route, but harmless/idempotent here too —
    // covers routes with no screenCode and keeps the menu populated defensively.
    await this.permission.ensureLoaded();

    this.notificationsApi.list(false, 20).subscribe({ next: (n) => this.notifications.set(n) });
    this.notificationsApi.unreadCount().subscribe({ next: (c) => this.unreadCount.set(c) });

    await this.hub.start();
    this.hub.on<NotificationCreatedEvent>('NotificationCreated', (e) => {
      this.notifications.update((list) => [
        { id: e.id, title: e.title, message: e.message, severity: e.severity, isRead: false, createdAt: e.createdAt },
        ...list,
      ].slice(0, 20));
      this.unreadCount.update((c) => c + 1);
    });
  }

  markRead(n: AppNotification): void {
    if (n.isRead) return;
    this.notificationsApi.markRead(n.id).subscribe({
      next: () => {
        this.notifications.update((list) => list.map((x) => (x.id === n.id ? { ...x, isRead: true } : x)));
        this.unreadCount.update((c) => Math.max(0, c - 1));
      },
    });
  }

  markAllRead(): void {
    this.notificationsApi.markAllRead().subscribe({
      next: () => {
        this.notifications.update((list) => list.map((x) => ({ ...x, isRead: true })));
        this.unreadCount.set(0);
      },
    });
  }

  toggleSidebar(): void {
    this.sidebarOpen.update((v) => !v);
  }

  logout(): void {
    this.auth.logout();
  }
}
