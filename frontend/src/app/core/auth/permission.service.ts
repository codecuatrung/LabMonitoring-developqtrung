import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ScreenSummary } from '../models/screens.models';

/**
 * The current user's resolved screen access (SRS FR-8 4-layer permission model: role + per-user
 * override, merged server-side). Loaded once (memoized) and reused by both the sidebar menu and
 * `screenGuard` — a single source of truth, since hiding a menu item without the API also refusing
 * the same screen would just be security-through-obscurity.
 */
@Injectable({ providedIn: 'root' })
export class PermissionService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  private readonly _screens = signal<ScreenSummary[]>([]);
  readonly screens = this._screens.asReadonly();

  private loadPromise: Promise<void> | null = null;

  /** Fetches (once) and waits for the current user's accessible screens. Safe to call repeatedly —
   * subsequent calls reuse the in-flight/completed request. */
  ensureLoaded(): Promise<void> {
    this.loadPromise ??= firstValueFrom(this.http.get<ScreenSummary[]>(`${this.base}/me/screens`))
      .then((screens) => {
        this._screens.set(screens);
      })
      .catch((err) => {
        this.loadPromise = null; // allow retry on the next guard/menu check
        throw err;
      });
    return this.loadPromise;
  }

  canAccess(screenCode: string): boolean {
    return this._screens().some((s) => s.code === screenCode);
  }

  /** Call on logout so a subsequent sign-in doesn't briefly show the previous user's menu. */
  reset(): void {
    this.loadPromise = null;
    this._screens.set([]);
  }
}
