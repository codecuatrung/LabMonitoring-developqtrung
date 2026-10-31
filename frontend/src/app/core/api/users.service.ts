import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PagedResult } from '../models/paging.models';

export interface UserSummary {
  id: number;
  username: string;
  fullName: string;
  email: string | null;
  isActive: boolean;
  roles: string[];
}

export interface ScreenOverride {
  screenId: number;
  isGranted: boolean;
}

export interface UserDetail {
  id: number;
  username: string;
  fullName: string;
  email: string | null;
  phone: string | null;
  isActive: boolean;
  roleIds: number[];
  roomScopeIds: number[];
  screenOverrides: ScreenOverride[];
}

export interface UpdateUserInput {
  fullName: string;
  phone: string | null;
  isActive: boolean;
}

/** User/role/room-scope/screen-override management (SRS FR-8.1/8.2). No create — accounts are
 * provisioned by Keycloak sign-in; this only edits an already-provisioned account. */
@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  list(params: { role?: string; includeInactive?: boolean; page?: number; pageSize?: number } = {}): Observable<PagedResult<UserSummary>> {
    const q = new URLSearchParams();
    if (params.role) q.set('role', params.role);
    if (params.includeInactive) q.set('includeInactive', 'true');
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString();
    return this.http.get<PagedResult<UserSummary>>(`${this.base}/users${qs ? '?' + qs : ''}`);
  }

  get(id: number): Observable<UserDetail> {
    return this.http.get<UserDetail>(`${this.base}/users/${id}`);
  }

  update(id: number, input: UpdateUserInput): Observable<void> {
    return this.http.put<void>(`${this.base}/users/${id}`, input);
  }

  setRoles(id: number, roleIds: number[]): Observable<void> {
    return this.http.put<void>(`${this.base}/users/${id}/roles`, roleIds);
  }

  setRoomScopes(id: number, roomIds: number[]): Observable<void> {
    return this.http.put<void>(`${this.base}/users/${id}/room-scopes`, roomIds);
  }

  setScreenOverrides(id: number, overrides: ScreenOverride[]): Observable<void> {
    return this.http.put<void>(`${this.base}/users/${id}/screen-overrides`, overrides);
  }
}
