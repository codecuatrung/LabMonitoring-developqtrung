import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AppNotification } from '../models/notifications.models';

/** The caller's own in-app notifications (SRS FR-7.2). */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  list(unreadOnly = false, limit = 50): Observable<AppNotification[]> {
    return this.http.get<AppNotification[]>(`${this.base}/notifications?unreadOnly=${unreadOnly}&limit=${limit}`);
  }

  unreadCount(): Observable<number> {
    return this.http.get<number>(`${this.base}/notifications/unread-count`);
  }

  markRead(id: number): Observable<void> {
    return this.http.post<void>(`${this.base}/notifications/${id}/read`, {});
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>(`${this.base}/notifications/read-all`, {});
  }
}
