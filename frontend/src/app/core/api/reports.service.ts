import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { UsageReport, UsageReportFilter } from '../models/reports.models';

/** Usage reports (SRS FR-7.4). */
@Injectable({ providedIn: 'root' })
export class ReportsService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  usage(filter: UsageReportFilter): Observable<UsageReport> {
    return this.http.get<UsageReport>(`${this.base}/reports/usage${this.query(filter)}`);
  }

  exportUsage(filter: UsageReportFilter): Observable<Blob> {
    return this.http.get(`${this.base}/reports/usage/export${this.query(filter)}`, { responseType: 'blob' });
  }

  private query(filter: UsageReportFilter): string {
    const q = new URLSearchParams();
    if (filter.roomId) q.set('roomId', String(filter.roomId));
    if (filter.classId) q.set('classId', String(filter.classId));
    if (filter.from) q.set('from', filter.from);
    if (filter.to) q.set('to', filter.to);
    const qs = q.toString();
    return qs ? `?${qs}` : '';
  }
}
