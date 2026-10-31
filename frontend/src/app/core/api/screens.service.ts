import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ScreenSummary } from '../models/screens.models';

export interface ScreenInput {
  code: string;
  name: string;
  route: string | null;
  icon: string | null;
  displayOrder: number;
  parentId: number | null;
}
export interface UpdateScreenInput {
  name: string;
  route: string | null;
  icon: string | null;
  displayOrder: number;
  isActive: boolean;
  parentId: number | null;
}

/** Function screen registry (SRS FR-8 "màn hình chức năng"). */
@Injectable({ providedIn: 'root' })
export class ScreensService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  list(): Observable<ScreenSummary[]> {
    return this.http.get<ScreenSummary[]>(`${this.base}/screens`);
  }

  create(input: ScreenInput): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(`${this.base}/screens`, input);
  }

  update(id: number, input: UpdateScreenInput): Observable<void> {
    return this.http.put<void>(`${this.base}/screens/${id}`, input);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/screens/${id}`);
  }
}
