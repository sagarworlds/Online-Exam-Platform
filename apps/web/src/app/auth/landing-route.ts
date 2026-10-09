import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { ADMIN_SECTIONS } from './admin-sections';
import { AuthSessionService } from './auth-session.service';

/** Where a signed-in user starts: staff with an admin area open to them land on the admin home, everyone else on their exams. */
export function landingRoute(authSession: AuthSessionService): string {
  return authSession.hasAnyPermission(ADMIN_SECTIONS.map((section) => section.permission)) ? '/admin' : '/my-exams';
}

/** Redirects the site root to the signed-in user's landing page, or to /login when nobody is signed in. */
export const landingGuard: CanActivateFn = () => {
  const authSession = inject(AuthSessionService);
  const router = inject(Router);

  return router.parseUrl(authSession.isAuthenticated() ? landingRoute(authSession) : '/login');
};
