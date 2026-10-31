import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Role {
  id: number;
  name: string;
  isSystem: boolean;
  screenIds: number[];
}

/** Role CRUD + role→screen assignment (SRS FR-8.1). Any number of custom roles can be created; the
 * 3 seeded roles (Administrator/Technician/Teacher) can't be renamed or deleted (enforced server-side). */
@Injectable({ providedIn: 'root' })
export class RolesService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  list(): Observable<Role[]> {
    return this.http.get<Role[]>(`${this.base}/roles`);
  }

  create(name: string): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(`${this.base}/roles`, { name });
  }

  update(id: number, name: string): Observable<void> {
    return this.http.put<void>(`${this.base}/roles/${id}`, { name });
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/roles/${id}`);
  }

  setScreens(id: number, screenIds: number[]): Observable<void> {
    return this.http.put<void>(`${this.base}/roles/${id}/screens`, screenIds);
  }
}
