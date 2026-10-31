import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuditLogsService } from '../../../core/api/audit-logs.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import { AuditLogEntry } from '../../../core/models/audit-logs.models';
import { PagedResult } from '../../../core/models/paging.models';
import { PaginationComponent } from '../../shared/pagination/pagination';

@Component({
  selector: 'app-audit-logs-list',
  imports: [FormsModule, DatePipe, PaginationComponent],
  templateUrl: './audit-logs-list.html',
})
export class AuditLogsListComponent implements OnInit {
  private readonly auditApi = inject(AuditLogsService);
  private readonly pageTitle = inject(PageTitleService);

  readonly result = signal<PagedResult<AuditLogEntry> | null>(null);
  readonly error = signal<string | null>(null);

  action = '';
  page = 1;
  pageSize = 20;

  ngOnInit(): void {
    this.pageTitle.set('Nhật ký hệ thống');
    this.run();
  }

  run(): void {
    this.page = 1;
    this.load();
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
    this.auditApi.list({ action: this.action || undefined, page: this.page, pageSize: this.pageSize }).subscribe({
      next: (r) => this.result.set(r),
      error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không tải được nhật ký'),
    });
  }
}
