import { AuthConfig } from 'angular-oauth2-oidc';
import { environment } from '../../../environments/environment';

/**
 * Keycloak (realm `coreproject`) — Authorization Code + PKCE for the public SPA client.
 * `sessionChecksEnabled: false` because bsc./sso. are different subdomains and the
 * session-check iframe is blocked by third-party-cookie rules (see KEYCLOAK-INTEGRATION.md).
 */
export const authConfig: AuthConfig = {
  issuer: environment.keycloak.issuer,
  clientId: environment.keycloak.clientId,
  redirectUri: window.location.origin + '/',
  postLogoutRedirectUri: window.location.origin + '/',
  responseType: 'code',
  scope: 'openid profile email',
  requireHttps: environment.production,
  showDebugInformation: !environment.production,
  sessionChecksEnabled: false,
  clearHashAfterLogin: true,
  useSilentRefresh: false,
  timeoutFactor: 0.75,
};
