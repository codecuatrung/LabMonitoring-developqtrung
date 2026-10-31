import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SessionDetail, SessionReport, SessionSummary } from '../models/sessions.models';
import { PagedResult } from '../models/paging.models';

export interface StudentAssignment {
  computerId: number;
  studentId: number | null;
}

@Injectable({ providedIn: 'root' })
export class SessionsService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  list(params: { roomId?: number; classId?: number; status?: string; page?: number; pageSize?: number } = {}): Observable<PagedResult<SessionSummary>> {
    const q = new URLSearchParams();
    if (params.roomId) q.set('roomId', String(params.roomId));
    if (params.classId) q.set('classId', String(params.classId));
    if (params.status) q.set('status', params.status);
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString();
    return this.http.get<PagedResult<SessionSummary>>(`${this.base}/sessions${qs ? '?' + qs : ''}`);
  }

  get(id: number): Observable<SessionDetail> {
    return this.http.get<SessionDetail>(`${this.base}/sessions/${id}`);
  }

  report(id: number): Observable<SessionReport> {
    return this.http.get<SessionReport>(`${this.base}/sessions/${id}/report`);
  }

  start(roomId: number, classId: number, scheduleId: number | null = null): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(`${this.base}/sessions/start`, { roomId, classId, scheduleId });
  }

  end(id: number): Observable<void> {
    return this.http.post<void>(`${this.base}/sessions/${id}/end`, {});
  }

  allocateAuto(id: number): Observable<void> {
    return this.http.post<void>(`${this.base}/sessions/${id}/allocate`, { auto: true, assignments: null });
  }

  allocateManual(id: number, assignments: StudentAssignment[]): Observable<void> {
    return this.http.post<void>(`${this.base}/sessions/${id}/allocate`, { auto: false, assignments });
  }
}
