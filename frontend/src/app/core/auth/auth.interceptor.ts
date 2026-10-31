import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';
import { environment } from '../../../environments/environment';

/** Attaches the Keycloak bearer token to calls hitting our own API. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const isApiCall = req.url.startsWith(environment.apiBaseUrl) || req.url.includes('/api/');

  if (isApiCall && auth.hasValidToken()) {
    req = req.clone({ setHeaders: { Authorization: `Bearer ${auth.accessToken}` } });
  }

  return next(req);
};
