import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { SessionsService } from '../../../core/api/sessions.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import { SessionSummary } from '../../../core/models/sessions.models';
import { PaginationComponent } from '../../shared/pagination/pagination';

@Component({
  selector: 'app-sessions-list',
  imports: [RouterLink, DatePipe, PaginationComponent],
  templateUrl: './sessions-list.html',
})
export class SessionsList implements OnInit {
  private readonly sessionsApi = inject(SessionsService);
  private readonly pageTitle = inject(PageTitleService);

  readonly sessions = signal<SessionSummary[]>([]);
  readonly totalCount = signal(0);
  readonly error = signal<string | null>(null);

  page = 1;
  pageSize = 20;

  ngOnInit(): void {
    this.pageTitle.set('Ca thực hành');
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
    this.sessionsApi.list({ page: this.page, pageSize: this.pageSize }).subscribe({
      next: (r) => {
        this.sessions.set(r.items);
        this.totalCount.set(r.totalCount);
      },
      error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không tải được danh sách ca thực hành'),
    });
  }
}
