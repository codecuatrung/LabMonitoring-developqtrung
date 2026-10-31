import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FacilitiesService } from '../../../core/api/facilities.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import { ComputerDetail, MetricPoint } from '../../../core/models/facilities.models';
import { CommandResultEvent } from '../../../core/models/commands.models';
import { PagedResult } from '../../../core/models/paging.models';
import { statusClass, statusLabel } from '../../../core/models/status-ui';
import { LabHubService } from '../../../core/realtime/lab-hub.service';
import { CommandPanelComponent } from '../../shared/command-panel/command-panel';
import { PaginationComponent } from '../../shared/pagination/pagination';

@Component({
  selector: 'app-computer-detail',
  imports: [RouterLink, DatePipe, DecimalPipe, FormsModule, CommandPanelComponent, PaginationComponent],
  templateUrl: './computer-detail.html',
})
export class ComputerDetailComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly facilities = inject(FacilitiesService);
  private readonly hub = inject(LabHubService);
  private readonly pageTitle = inject(PageTitleService);

  readonly statusClass = statusClass;
  readonly statusLabel = statusLabel;

  readonly computer = signal<ComputerDetail | null>(null);
  readonly metricsResult = signal<PagedResult<MetricPoint> | null>(null);
  readonly error = signal<string | null>(null);
  readonly commandResults = signal<CommandResultEvent[]>([]);

  metricsPage = 1;
  metricsPageSize = 20;

  readonly showDeleteMetricsModal = signal(false);
  readonly deletingMetrics = signal(false);
  readonly deleteMetricsError = signal<string | null>(null);
  readonly deleteMetricsMessage = signal<string | null>(null);
  deleteMetricsBefore = '';

  private computerId = 0;
  private subscribedRoomId: number | null = null;

  ngOnInit(): void {
    this.computerId = Number(this.route.snapshot.paramMap.get('id'));
    this.facilities.computer(this.computerId).subscribe({
      next: async (c) => {
        this.computer.set(c);
        this.pageTitle.set(c.name);
        await this.hub.start();
        this.subscribedRoomId = c.roomId;
        await this.hub.subscribeToRoom(c.roomId);
        this.hub.on<CommandResultEvent>('CommandResultReceived', (e) =>
          this.commandResults.update((list) => [e, ...list].slice(0, 50)),
        );
      },
      error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không tải được máy'),
    });
    this.loadMetrics();
  }

  onMetricsPageChange(page: number): void {
    this.metricsPage = page;
    this.loadMetrics();
  }

  onMetricsPageSizeChange(pageSize: number): void {
    this.metricsPageSize = pageSize;
    this.metricsPage = 1;
    this.loadMetrics();
  }

  private loadMetrics(): void {
    this.facilities
      .computerMetrics(this.computerId, { page: this.metricsPage, pageSize: this.metricsPageSize })
      .subscribe({ next: (r) => this.metricsResult.set(r) });
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
    this.facilities.deleteComputerMetrics(this.computerId, before).subscribe({
      next: (deletedCount) => {
        this.deletingMetrics.set(false);
        this.showDeleteMetricsModal.set(false);
        this.deleteMetricsMessage.set(`Đã xoá ${deletedCount} bản ghi.`);
        this.metricsPage = 1;
        this.loadMetrics();
      },
      error: (e) => {
        this.deletingMetrics.set(false);
        this.deleteMetricsError.set(e?.error?.detail ?? e?.message ?? 'Không xoá được lịch sử tài nguyên');
      },
    });
  }

  ngOnDestroy(): void {
    this.hub.off('CommandResultReceived');
    if (this.subscribedRoomId != null) {
      this.hub.unsubscribeFromRoom(this.subscribedRoomId);
    }
  }
}
