import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  Building,
  ComputerDetail,
  DashboardSummary,
  MetricPoint,
  Room,
  RoomComputer,
  RoomDetail,
} from '../models/facilities.models';
import { PagedResult } from '../models/paging.models';

export interface BuildingInput {
  name: string;
  code: string;
}
export interface RoomInput {
  buildingId: number;
  name: string;
  code: string;
  capacity: number;
  mapImageUrl: string | null;
}

@Injectable({ providedIn: 'root' })
export class FacilitiesService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  dashboardSummary(): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>(`${this.base}/dashboard/summary`);
  }

  // Buildings
  buildings(params: { page?: number; pageSize?: number } = {}): Observable<PagedResult<Building>> {
    const q = new URLSearchParams();
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString();
    return this.http.get<PagedResult<Building>>(`${this.base}/buildings${qs ? '?' + qs : ''}`);
  }
  createBuilding(input: BuildingInput): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(`${this.base}/buildings`, input);
  }
  updateBuilding(id: number, input: BuildingInput): Observable<void> {
    return this.http.put<void>(`${this.base}/buildings/${id}`, input);
  }
  deleteBuilding(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/buildings/${id}`);
  }

  // Rooms
  rooms(params: { buildingId?: number; page?: number; pageSize?: number } = {}): Observable<PagedResult<Room>> {
    const q = new URLSearchParams();
    if (params.buildingId) q.set('buildingId', String(params.buildingId));
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString();
    return this.http.get<PagedResult<Room>>(`${this.base}/rooms${qs ? '?' + qs : ''}`);
  }
  room(id: number): Observable<RoomDetail> {
    return this.http.get<RoomDetail>(`${this.base}/rooms/${id}`);
  }
  createRoom(input: RoomInput): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(`${this.base}/rooms`, input);
  }
  updateRoom(id: number, input: RoomInput): Observable<void> {
    return this.http.put<void>(`${this.base}/rooms/${id}`, input);
  }
  deleteRoom(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/rooms/${id}`);
  }
  regenerateRoomCode(id: number): Observable<{ code: string }> {
    return this.http.post<{ code: string }>(`${this.base}/rooms/${id}/regenerate-code`, {});
  }
  /** Purge resource-history rows for every computer in this room; omit `before` to delete everything. */
  deleteRoomMetrics(id: number, before?: Date): Observable<number> {
    const q = before ? `?before=${before.toISOString()}` : '';
    return this.http.delete<number>(`${this.base}/rooms/${id}/metrics${q}`);
  }

  // Computers
  computers(params: { roomId?: number; status?: string; pendingApproval?: boolean } = {}): Observable<RoomComputer[]> {
    const q = new URLSearchParams();
    if (params.roomId) q.set('roomId', String(params.roomId));
    if (params.status) q.set('status', params.status);
    if (params.pendingApproval) q.set('pendingApproval', 'true');
    const qs = q.toString();
    return this.http.get<RoomComputer[]>(`${this.base}/computers${qs ? '?' + qs : ''}`);
  }
  computer(id: number): Observable<ComputerDetail> {
    return this.http.get<ComputerDetail>(`${this.base}/computers/${id}`);
  }
  computerMetrics(
    id: number,
    params: { from?: Date; to?: Date; page?: number; pageSize?: number } = {},
  ): Observable<PagedResult<MetricPoint>> {
    const q = new URLSearchParams();
    if (params.from) q.set('from', params.from.toISOString());
    if (params.to) q.set('to', params.to.toISOString());
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString();
    return this.http.get<PagedResult<MetricPoint>>(`${this.base}/computers/${id}/metrics${qs ? '?' + qs : ''}`);
  }
  /** Purge resource-history rows for this machine; omit `before` to delete everything. Returns the deleted count. */
  deleteComputerMetrics(id: number, before?: Date): Observable<number> {
    const q = before ? `?before=${before.toISOString()}` : '';
    return this.http.delete<number>(`${this.base}/computers/${id}/metrics${q}`);
  }
  approveComputer(id: number): Observable<void> {
    return this.http.post<void>(`${this.base}/computers/${id}/approve`, {});
  }
  rejectComputer(id: number): Observable<void> {
    return this.http.post<void>(`${this.base}/computers/${id}/reject`, {});
  }
  deleteComputer(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/computers/${id}`);
  }
}
