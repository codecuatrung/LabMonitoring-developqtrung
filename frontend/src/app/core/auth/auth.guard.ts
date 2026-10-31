import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { AuthService } from './auth.service';

/** Redirects to Keycloak when there is no valid session. */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);

  if (auth.hasValidToken()) {
    return true;
  }

  auth.login();
  return false;
};
