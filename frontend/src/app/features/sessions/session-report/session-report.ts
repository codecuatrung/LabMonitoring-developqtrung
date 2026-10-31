import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { SessionsService } from '../../../core/api/sessions.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import { SessionReport } from '../../../core/models/sessions.models';

@Component({
  selector: 'app-session-report',
  imports: [RouterLink, DatePipe, DecimalPipe],
  templateUrl: './session-report.html',
})
export class SessionReportComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly sessionsApi = inject(SessionsService);
  private readonly pageTitle = inject(PageTitleService);

  readonly report = signal<SessionReport | null>(null);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.sessionsApi.report(id).subscribe({
      next: (r) => {
        this.report.set(r);
        this.pageTitle.set(`Báo cáo ca thực hành — ${r.className}`);
      },
      error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không tải được báo cáo'),
    });
  }
}
