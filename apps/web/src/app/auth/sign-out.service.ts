import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthApiService } from './auth-api.service';
import { AuthSessionService } from './auth-session.service';

/**
 * Ends the signed-in session and returns to /login. Used by the nav bar's Log out and by the
 * "sign in with a different account" action on the no-access page, so both behave the same way.
 */
@Injectable({ providedIn: 'root' })
export class SignOutService {
  private readonly authApi = inject(AuthApiService);
  private readonly authSession = inject(AuthSessionService);
  private readonly router = inject(Router);

  /** Revokes the session on the server, then always clears it locally and returns to /login. */
  signOut(): void {
    // Local state is cleared in finalize, i.e. on success *and* failure: signing
    // out must always work on this device. A failed call is expected when the
    // session was already revoked or superseded (401), and if the API is
    // unreachable the server session still lapses at its own expiry - so the
    // error needs no handling beyond not being reported as unhandled.
    this.authApi
      .logout()
      .pipe(
        finalize(() => {
          this.authSession.logout();
          // A guard only re-runs on navigation, not reactively - without this, logging
          // out while on a guarded page (Profile/Consent) would leave its stale content
          // on screen even though the nav bar itself updates.
          void this.router.navigateByUrl('/login');
        }),
      )
      .subscribe({ error: () => undefined });
  }
}
