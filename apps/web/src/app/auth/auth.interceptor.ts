import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthSessionService } from './auth-session.service';

/**
 * Attaches the bearer token to every request when signed in. On a 401 for a
 * request that *was* authenticated, treats it as "the session is no longer
 * valid" and signs out — but a 401 with no token attached (e.g. wrong
 * password on the login form itself) is left alone, since there is no
 * session to lose and redirecting away would just hide the form's own error.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const authSession = inject(AuthSessionService);
  const router = inject(Router);

  const token = authSession.accessToken;
  const authorizedRequest =
    token === null ? request : request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });

  return next(authorizedRequest).pipe(
    catchError((error: unknown) => {
      if (token !== null && error instanceof HttpErrorResponse && error.status === 401) {
        authSession.logout();
        void router.navigateByUrl('/login');
      }

      return throwError(() => error);
    }),
  );
};
