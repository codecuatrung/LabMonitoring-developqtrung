import { Component, OnInit, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ReportsService } from '../../../core/api/reports.service';
import { FacilitiesService } from '../../../core/api/facilities.service';
import { TeachingService } from '../../../core/api/teaching.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import { Room } from '../../../core/models/facilities.models';
import { ClassSummary } from '../../../core/models/teaching.models';
import { UsageReport } from '../../../core/models/reports.models';

@Component({
  selector: 'app-usage-report',
  imports: [FormsModule, DecimalPipe],
  templateUrl: './usage-report.html',
})
export class UsageReportComponent implements OnInit {
  private readonly reportsApi = inject(ReportsService);
  private readonly facilities = inject(FacilitiesService);
  private readonly teaching = inject(TeachingService);
  private readonly pageTitle = inject(PageTitleService);

  readonly rooms = signal<Room[]>([]);
  readonly classes = signal<ClassSummary[]>([]);
  readonly report = signal<UsageReport | null>(null);
  readonly error = signal<string | null>(null);
  readonly loading = signal(false);

  roomId = 0;
  classId = 0;
  from = '';
  to = '';

  ngOnInit(): void {
    this.pageTitle.set('Báo cáo sử dụng');
    this.facilities.rooms({ pageSize: 500 }).subscribe({ next: (r) => this.rooms.set(r.items) });
    this.teaching.classes({ pageSize: 500 }).subscribe({ next: (r) => this.classes.set(r.items) });
    this.run();
  }

  run(): void {
    this.loading.set(true);
    this.error.set(null);
    this.reportsApi
      .usage({
        roomId: this.roomId || undefined,
        classId: this.classId || undefined,
        from: this.from ? new Date(this.from).toISOString() : undefined,
        to: this.to ? new Date(this.to).toISOString() : undefined,
      })
      .subscribe({
        next: (r) => {
          this.report.set(r);
          this.loading.set(false);
        },
        error: (e) => {
          this.error.set(e?.error?.detail ?? e?.message ?? 'Không tải được báo cáo');
          this.loading.set(false);
        },
      });
  }

  export(): void {
    this.reportsApi
      .exportUsage({
        roomId: this.roomId || undefined,
        classId: this.classId || undefined,
        from: this.from ? new Date(this.from).toISOString() : undefined,
        to: this.to ? new Date(this.to).toISOString() : undefined,
      })
      .subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = `bao-cao-su-dung-${new Date().toISOString().slice(0, 10)}.csv`;
          a.click();
          URL.revokeObjectURL(url);
        },
      });
  }
}
