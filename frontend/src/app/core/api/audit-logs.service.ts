import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuditLogEntry, AuditLogFilter } from '../models/audit-logs.models';
import { PagedResult } from '../models/paging.models';

/** Audit trail search (SRS FR-8.3). */
@Injectable({ providedIn: 'root' })
export class AuditLogsService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  list(filter: AuditLogFilter): Observable<PagedResult<AuditLogEntry>> {
    const q = new URLSearchParams();
    if (filter.userId) q.set('userId', String(filter.userId));
    if (filter.action) q.set('action', filter.action);
    if (filter.from) q.set('from', filter.from);
    if (filter.to) q.set('to', filter.to);
    if (filter.page) q.set('page', String(filter.page));
    if (filter.pageSize) q.set('pageSize', String(filter.pageSize));
    const qs = q.toString();
    return this.http.get<PagedResult<AuditLogEntry>>(`${this.base}/audit-logs${qs ? '?' + qs : ''}`);
  }
}
