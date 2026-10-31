import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AgentPackage {
  id: number;
  version: string;
  fileName: string;
  fileSizeBytes: number;
  sha256: string;
  uploadedByName: string | null;
  uploadedAt: string;
}

/** Uploaded LabAgent build history (SRS H1 "auto-update"). The most recently uploaded package is
 * always the one an "Update Agent" command pushes — there's no separate "activate" step. */
@Injectable({ providedIn: 'root' })
export class AgentUpdatesService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  list(): Observable<AgentPackage[]> {
    return this.http.get<AgentPackage[]>(`${this.base}/agent-updates`);
  }

  upload(version: string, file: File): Observable<{ id: number }> {
    const form = new FormData();
    form.append('version', version);
    form.append('file', file);
    return this.http.post<{ id: number }>(`${this.base}/agent-updates`, form);
  }
}
