import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { finalize } from 'rxjs';
import { ADMIN_SECTIONS } from './auth/admin-sections';
import { AuthApiService } from './auth/auth-api.service';
import { AuthSessionService } from './auth/auth-session.service';
import { HealthService } from './health/health.service';
import { AdminSidebar } from './shared/admin-sidebar/admin-sidebar';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, AdminSidebar],
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  private readonly healthService = inject(HealthService);
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  protected readonly authSession = inject(AuthSessionService);

  /** The admin areas this user's permissions open; empty for a candidate, so the nav shows no admin links to them. */
  protected readonly adminSections = computed(() =>
    ADMIN_SECTIONS.filter((section) => this.authSession.hasPermission(section.permission)),
  );

  protected readonly health = toSignal(this.healthService.checkHealth(), {
    initialValue: { state: 'checking' as const, reason: null },
  });

  /** Revokes the session on the server, then always clears it locally and returns to /login. */
  protected logout(): void {
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
