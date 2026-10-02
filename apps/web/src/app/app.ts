import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { ADMIN_SECTIONS } from './auth/admin-sections';
import { AuthSessionService } from './auth/auth-session.service';
import { HealthService } from './health/health.service';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet],
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  private readonly healthService = inject(HealthService);
  private readonly router = inject(Router);
  protected readonly authSession = inject(AuthSessionService);

  /** The admin areas this user's permissions open; empty for a candidate, so the nav shows no admin links to them. */
  protected readonly adminSections = computed(() =>
    ADMIN_SECTIONS.filter((section) => this.authSession.hasPermission(section.permission)),
  );

  protected readonly health = toSignal(this.healthService.checkHealth(), {
    initialValue: { state: 'checking' as const, reason: null },
  });

  protected logout(): void {
    this.authSession.logout();
    // A guard only re-runs on navigation, not reactively - without this, logging
    // out while on a guarded page (Profile/Consent) would leave its stale content
    // on screen even though the nav bar itself updates.
    void this.router.navigateByUrl('/login');
  }
}
