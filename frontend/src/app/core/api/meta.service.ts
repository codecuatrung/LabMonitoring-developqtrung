import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ApiMeta {
  service: string;
  version: string | null;
  environmentName: string;
  utcNow: string;
}

@Injectable({ providedIn: 'root' })
export class MetaService {
  private readonly http = inject(HttpClient);

  get(): Observable<ApiMeta> {
    return this.http.get<ApiMeta>(`${environment.apiBaseUrl}/meta`);
  }
}
