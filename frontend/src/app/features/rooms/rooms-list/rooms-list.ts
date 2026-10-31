import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { FacilitiesService } from '../../../core/api/facilities.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import { Room } from '../../../core/models/facilities.models';

/** One slice of a room's status bar (share of its machines in a given state). */
export interface StatusSegment {
  key: 'online' | 'inuse' | 'warning' | 'offline';
  label: string;
  count: number;
  percent: number;
}

/**
 * Rooms as a responsive grid of cards. Each card shows the room's identity plus a segmented status bar
 * (online / in use / warning / offline) so a whole floor can be scanned at a glance; the whole card is the
 * click target. All rooms are loaded at once (no pagination); search and building filter apply client-side.
 */
@Component({
  selector: 'app-rooms-list',
  imports: [RouterLink, FormsModule],
  templateUrl: './rooms-list.html',
})
export class RoomsList implements OnInit {
  private readonly facilities = inject(FacilitiesService);
  private readonly pageTitle = inject(PageTitleService);

  readonly rooms = signal<Room[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  // Filters are signals (not plain fields) so the computed() below re-evaluates when they change.
  readonly search = signal('');
  readonly building = signal('');

  readonly buildings = computed(() => [...new Set(this.rooms().map((r) => r.buildingName))].sort());

  readonly visibleRooms = computed(() => {
    const q = this.search().trim().toLowerCase();
    const b = this.building();
    return this.rooms().filter(
      (r) =>
        (!b || r.buildingName === b) &&
        (!q || r.name.toLowerCase().includes(q) || r.code.toLowerCase().includes(q)),
    );
  });

  /** Totals over the rooms currently on screen. */
  readonly totals = computed(() => {
    const list = this.visibleRooms();
    const sum = (f: (r: Room) => number) => list.reduce((acc, r) => acc + f(r), 0);
    return {
      rooms: list.length,
      computers: sum((r) => r.computerCount),
      up: sum((r) => r.onlineCount + r.inUseCount + r.warningCount),
      inUse: sum((r) => r.inUseCount),
      pending: sum((r) => r.pendingCount),
    };
  });

  ngOnInit(): void {
    this.pageTitle.set('Phòng máy');
    this.load();
  }

  clearFilters(): void {
    this.search.set('');
    this.building.set('');
  }

  /** Approved machines in the room (the ones the status bar describes). */
  approvedCount(r: Room): number {
    return Math.max(0, r.computerCount - r.pendingCount);
  }

  segments(r: Room): StatusSegment[] {
    const total = this.approvedCount(r);
    const offline = Math.max(0, total - r.onlineCount - r.inUseCount - r.warningCount);
    const raw: Omit<StatusSegment, 'percent'>[] = [
      { key: 'online', label: 'Online', count: r.onlineCount },
      { key: 'inuse', label: 'Đang dùng', count: r.inUseCount },
      { key: 'warning', label: 'Cảnh báo', count: r.warningCount },
      { key: 'offline', label: 'Offline', count: offline },
    ];
    return raw.map((s) => ({ ...s, percent: total > 0 ? (s.count / total) * 100 : 0 }));
  }

  private load(): void {
    this.loading.set(true);
    // One screen of cards: fetch every room the user can see (the API caps a page at 500).
    this.facilities.rooms({ pageSize: 500 }).subscribe({
      next: (r) => {
        this.rooms.set(r.items);
        this.loading.set(false);
      },
      error: (e) => {
        this.error.set(e?.error?.detail ?? e?.message ?? 'Không tải được danh sách phòng');
        this.loading.set(false);
      },
    });
  }
}
