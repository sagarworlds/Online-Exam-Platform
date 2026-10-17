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
  return guardedBy((authSession) => authSession.hasPermission(permission));
}

/**
 * Like {@link permissionGuard}, for a page that several permissions open (such as the Settings page, which lists pages with different
 * permissions): holding any one of them is enough. Nobody is let in by an empty list.
 */
export function anyPermissionGuard(permissions: readonly string[]): CanActivateFn {
  return guardedBy((authSession) => authSession.hasAnyPermission(permissions));
}

/** The guard both of the above build: where a visitor goes is the same, only the question of who may enter differs. */
function guardedBy(mayEnter: (authSession: AuthSessionService) => boolean): CanActivateFn {
  return (_route, state) => {
    const authSession = inject(AuthSessionService);
    const router = inject(Router);

    if (!authSession.isAuthenticated()) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }

    return mayEnter(authSession) ? true : router.createUrlTree(['/forbidden']);
  };
}
