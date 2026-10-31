import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FacilitiesService } from '../../../core/api/facilities.service';
import { LabHubService } from '../../../core/realtime/lab-hub.service';
import { SessionsService } from '../../../core/api/sessions.service';
import { TeachingService } from '../../../core/api/teaching.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import {
  ComputerStatusChangedEvent,
  MetricsUpdatedEvent,
  RoomComputer,
  RoomDetail,
} from '../../../core/models/facilities.models';
import { CommandResultEvent } from '../../../core/models/commands.models';
import { statusClass, statusLabel } from '../../../core/models/status-ui';
import { ClassSummary } from '../../../core/models/teaching.models';
import { SessionSummary } from '../../../core/models/sessions.models';
import { CommandPanelComponent } from '../../shared/command-panel/command-panel';

@Component({
  selector: 'app-room-detail',
  imports: [RouterLink, DatePipe, DecimalPipe, FormsModule, CommandPanelComponent],
  templateUrl: './room-detail.html',
})
export class RoomDetailComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly facilities = inject(FacilitiesService);
  private readonly hub = inject(LabHubService);
  private readonly sessionsApi = inject(SessionsService);
  private readonly teaching = inject(TeachingService);
  private readonly pageTitle = inject(PageTitleService);

  readonly statusClass = statusClass;
  readonly statusLabel = statusLabel;

  readonly room = signal<RoomDetail | null>(null);
  readonly error = signal<string | null>(null);
  private roomId = 0;

  readonly pending = computed(() => this.room()?.computers.filter((c) => !c.isApproved) ?? []);
  readonly active = computed(() => this.room()?.computers.filter((c) => c.isApproved) ?? []);

  readonly activeSession = signal<SessionSummary | null>(null);
  readonly classes = signal<ClassSummary[]>([]);
  selectedClassId = 0;

  readonly commandResults = signal<CommandResultEvent[]>([]);

  readonly showDeleteMetricsModal = signal(false);
  readonly deletingMetrics = signal(false);
  readonly deleteMetricsError = signal<string | null>(null);
  readonly deleteMetricsMessage = signal<string | null>(null);
  deleteMetricsBefore = '';

  async ngOnInit(): Promise<void> {
    this.roomId = Number(this.route.snapshot.paramMap.get('id'));
    this.reload();
    this.reloadSession();
    this.teaching.classes({ pageSize: 500 }).subscribe({ next: (r) => this.classes.set(r.items) });

    await this.hub.start();
    await this.hub.subscribeToRoom(this.roomId);
    this.hub.on<ComputerStatusChangedEvent>('ComputerStatusChanged', (e) => this.applyStatus(e));
    this.hub.on<MetricsUpdatedEvent>('MetricsUpdated', (e) => this.applyMetrics(e));
    this.hub.on<CommandResultEvent>('CommandResultReceived', (e) =>
      this.commandResults.update((list) => [e, ...list].slice(0, 50)),
    );
  }

  reloadSession(): void {
    this.sessionsApi.list({ roomId: this.roomId, status: 'Active' }).subscribe({
      next: (r) => this.activeSession.set(r.items[0] ?? null),
    });
  }

  startSession(): void {
    if (!this.selectedClassId) return;
    this.sessionsApi.start(this.roomId, this.selectedClassId).subscribe({
      next: (res) => this.router.navigate(['/sessions', res.id]),
      error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không bắt đầu được ca thực hành'),
    });
  }

  ngOnDestroy(): void {
    this.hub.off('ComputerStatusChanged');
    this.hub.off('MetricsUpdated');
    this.hub.off('CommandResultReceived');
    this.hub.unsubscribeFromRoom(this.roomId);
  }

  reload(): void {
    this.facilities.room(this.roomId).subscribe({
      next: (r) => {
        this.room.set(r);
        this.pageTitle.set(`${r.name} (${r.code})`);
      },
      error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không tải được phòng'),
    });
  }

  openDeleteMetricsModal(): void {
    this.deleteMetricsBefore = '';
    this.deleteMetricsError.set(null);
    this.deleteMetricsMessage.set(null);
    this.showDeleteMetricsModal.set(true);
  }

  closeDeleteMetricsModal(): void {
    this.showDeleteMetricsModal.set(false);
  }

  confirmDeleteMetrics(): void {
    this.deletingMetrics.set(true);
    this.deleteMetricsError.set(null);
    const before = this.deleteMetricsBefore ? new Date(this.deleteMetricsBefore) : undefined;
    this.facilities.deleteRoomMetrics(this.roomId, before).subscribe({
      next: (deletedCount) => {
        this.deletingMetrics.set(false);
        this.showDeleteMetricsModal.set(false);
        this.deleteMetricsMessage.set(`Đã xoá ${deletedCount} bản ghi của cả phòng.`);
      },
      error: (e) => {
        this.deletingMetrics.set(false);
        this.deleteMetricsError.set(e?.error?.detail ?? e?.message ?? 'Không xoá được lịch sử tài nguyên');
      },
    });
  }

  approve(c: RoomComputer): void {
    this.facilities.approveComputer(c.id).subscribe({ next: () => this.reload() });
  }
  reject(c: RoomComputer): void {
    this.facilities.rejectComputer(c.id).subscribe({ next: () => this.reload() });
  }

  private patch(computerId: number, change: Partial<RoomComputer>): void {
    const r = this.room();
    if (!r) return;
    this.room.set({
      ...r,
      computers: r.computers.map((c) => (c.id === computerId ? { ...c, ...change } : c)),
    });
  }

  private applyStatus(e: ComputerStatusChangedEvent): void {
    if (e.roomId !== this.roomId) return;
    const known = this.room()?.computers.some((c) => c.id === e.computerId);
    if (!known) {
      this.reload();
      return;
    }
    this.patch(e.computerId, {
      status: e.status,
      currentWindowsUser: e.currentWindowsUser,
      lastHeartbeatAt: e.lastHeartbeatAt,
    });
  }

  private applyMetrics(e: MetricsUpdatedEvent): void {
    if (e.roomId !== this.roomId) return;
    this.patch(e.computerId, {
      cpuUsagePercent: e.cpuUsagePercent,
      ramUsagePercent: e.ramUsagePercent,
    });
  }
}
