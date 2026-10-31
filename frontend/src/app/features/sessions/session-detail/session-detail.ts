import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { SessionsService } from '../../../core/api/sessions.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import { SessionDetail } from '../../../core/models/sessions.models';

@Component({
  selector: 'app-session-detail',
  imports: [RouterLink, DatePipe],
  templateUrl: './session-detail.html',
})
export class SessionDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly sessionsApi = inject(SessionsService);
  private readonly pageTitle = inject(PageTitleService);

  readonly session = signal<SessionDetail | null>(null);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);

  private sessionId = 0;

  ngOnInit(): void {
    this.sessionId = Number(this.route.snapshot.paramMap.get('id'));
    this.reload();
  }

  reload(): void {
    this.sessionsApi.get(this.sessionId).subscribe({
      next: (s) => {
        this.session.set(s);
        this.pageTitle.set(`${s.className} — ${s.roomName}`);
      },
      error: (e) => this.error.set(e?.error?.detail ?? e?.message ?? 'Không tải được ca thực hành'),
    });
  }

  allocate(): void {
    this.busy.set(true);
    this.sessionsApi.allocateAuto(this.sessionId).subscribe({
      next: () => {
        this.busy.set(false);
        this.reload();
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(e?.error?.detail ?? e?.message ?? 'Phân bổ thất bại');
      },
    });
  }

  end(): void {
    this.busy.set(true);
    this.sessionsApi.end(this.sessionId).subscribe({
      next: () => this.router.navigate(['/sessions', this.sessionId, 'report']),
      error: (e) => {
        this.busy.set(false);
        this.error.set(e?.error?.detail ?? e?.message ?? 'Kết thúc ca thất bại');
      },
    });
  }
}
