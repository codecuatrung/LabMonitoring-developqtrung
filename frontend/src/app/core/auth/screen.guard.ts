import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { PermissionService } from './permission.service';

/** Blocks a route unless the current user can access its `data.screenCode` (SRS FR-8). Routes with
 * no `screenCode` are unrestricted (beyond authGuard). API-level authorization is the real security
 * boundary (ScreenPermissionHandler) — this only keeps the UI from navigating somewhere the API
 * would 403 anyway. */
export const screenGuard: CanActivateFn = async (route) => {
  const screenCode = route.data['screenCode'] as string | undefined;
  if (!screenCode) return true;

  const permission = inject(PermissionService);
  const router = inject(Router);

  try {
    await permission.ensureLoaded();
  } catch {
    return true; // couldn't resolve permissions (e.g. transient network error) — let the API be the judge
  }

  if (permission.canAccess(screenCode)) return true;

  // '/dashboard' is only a safe fallback if this user can actually reach it — a freshly
  // JIT-provisioned account with no role/override yet (or a custom role that omits "dashboard")
  // would otherwise bounce straight back into this same guard and get stuck. Fall back to the
  // user's own first accessible screen, or a dedicated no-access page if they have none at all.
  const screens = permission.screens();
  const fallbackRoute = screens.find((s) => s.route)?.route;
  return router.createUrlTree([fallbackRoute ?? '/no-access']);
};
