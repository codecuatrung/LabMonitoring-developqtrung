import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FacilitiesService } from '../../core/api/facilities.service';
import { LabHubService } from '../../core/realtime/lab-hub.service';
import { PageTitleService } from '../../core/layout/page-title.service';
import { DashboardSummary } from '../../core/models/facilities.models';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink],
  templateUrl: './dashboard.html',
})
export class Dashboard implements OnInit, OnDestroy {
  private readonly facilities = inject(FacilitiesService);
  private readonly pageTitle = inject(PageTitleService);
  readonly hub = inject(LabHubService);

  readonly summary = signal<DashboardSummary | null>(null);
  readonly error = signal<string | null>(null);
  private reloadTimer?: ReturnType<typeof setTimeout>;

  async ngOnInit(): Promise<void> {
    this.pageTitle.set('Bảng điều khiển');
    this.load();
    await this.hub.start();
    // Any status change / alert anywhere → refresh the counts (debounced).
    this.hub.on('ComputerStatusChanged', () => this.scheduleReload());
    this.hub.on('NewAlert', () => this.scheduleReload());
  }

  ngOnDestroy(): void {
    this.hub.off('ComputerStatusChanged');
    this.hub.off('NewAlert');
    clearTimeout(this.reloadTimer);
  }

  load(): void {
    this.facilities.dashboardSummary().subscribe({
      next: (s) => this.summary.set(s),
      error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không tải được số liệu'),
    });
  }

  private scheduleReload(): void {
    clearTimeout(this.reloadTimer);
    this.reloadTimer = setTimeout(() => this.load(), 800);
  }
}
