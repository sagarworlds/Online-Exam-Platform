import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthSessionService } from './auth-session.service';

/**
 * Guards a route behind a permission: signed-out visitors go to /login (keeping where they were headed),
 * signed-in users without the permission go to /forbidden.
 *
 * This only keeps people out of screens that would show them nothing; the permission is read from the token
 * on the client, so the API checks the same permission on every call and is what actually protects the data.
 */
export function permissionGuard(permission: string): CanActivateFn {
  return (_route, state) => {
    const authSession = inject(AuthSessionService);
    const router = inject(Router);

    if (!authSession.isAuthenticated()) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }

    return authSession.hasPermission(permission) ? true : router.createUrlTree(['/forbidden']);
  };
}
