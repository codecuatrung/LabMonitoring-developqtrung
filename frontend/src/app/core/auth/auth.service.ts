import { Injectable, computed, inject, signal } from '@angular/core';
import { OAuthService } from 'angular-oauth2-oidc';
import { authConfig } from './auth.config';

export interface LabUserProfile {
  sub: string;
  preferred_username?: string;
  name?: string;
  email?: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly oauth = inject(OAuthService);

  private readonly _profile = signal<LabUserProfile | null>(null);
  readonly profile = this._profile.asReadonly();
  readonly isAuthenticated = computed(() => this._profile() !== null);
  readonly displayName = computed(
    () => this._profile()?.name ?? this._profile()?.preferred_username ?? 'User',
  );

  /** Configure Keycloak, load discovery, and complete the code flow if we came back with a code. */
  async init(): Promise<void> {
    this.oauth.configure(authConfig);
    this.oauth.setupAutomaticSilentRefresh();

    try {
      await this.oauth.loadDiscoveryDocumentAndTryLogin();
    } catch (err) {
      console.error('Keycloak discovery/login failed', err);
      return;
    }

    if (this.oauth.hasValidAccessToken()) {
      this.refreshProfile();
    }
  }

  login(): void {
    this.oauth.initCodeFlow();
  }

  logout(): void {
    this._profile.set(null);
    this.oauth.logOut();
  }

  get accessToken(): string {
    return this.oauth.getAccessToken() ?? '';
  }

  hasValidToken(): boolean {
    return this.oauth.hasValidAccessToken();
  }

  private refreshProfile(): void {
    const claims = this.oauth.getIdentityClaims() as LabUserProfile | null;
    this._profile.set(claims ?? null);
  }
}
