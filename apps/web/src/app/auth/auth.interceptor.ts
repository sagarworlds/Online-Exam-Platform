import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails } from '../shared/problem-details';
import { AuthSessionService } from './auth-session.service';
import { SessionEndReason, isSessionEndReason } from './session-end-reason';

/**
 * Attaches the bearer token to every request when signed in. On a 401 for a
 * request that *was* authenticated, treats it as "the session is no longer
 * valid" and signs out — but a 401 with no token attached (e.g. wrong
 * password on the login form itself) is left alone, since there is no
 * session to lose and redirecting away would just hide the form's own error.
 * When the API says why the session ended (see {@link SessionEndReason}), the
 * reason is passed on as `/login?reason=<code>` so the login page can explain it.
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
        const reason = sessionEndReasonOf(error);
        void router.navigateByUrl(reason === null ? '/login' : `/login?reason=${reason}`);
      }

      return throwError(() => error);
    }),
  );
};

/**
 * Reads the API's reason for refusing the session from the 401's ProblemDetails
 * title. Only known codes are returned, so an arbitrary server string never ends
 * up in the URL or the login banner.
 */
function sessionEndReasonOf(error: HttpErrorResponse): SessionEndReason | null {
  const title = (error.error as ProblemDetails | null)?.title;
  return isSessionEndReason(title) ? title : null;
}
