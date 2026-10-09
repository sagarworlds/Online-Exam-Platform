import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ADMIN_SECTIONS } from './auth/admin-sections';
import { AuthSessionService } from './auth/auth-session.service';
import { SignOutService } from './auth/sign-out.service';
import { HealthService } from './health/health.service';
import { AdminSidebar } from './shared/admin-sidebar/admin-sidebar';
import { LanguageSwitcher } from './i18n/language-switcher';
import { TranslatePipe } from './i18n/translate.pipe';
import { ApiActivity } from './shared/api-activity/api-activity';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, AdminSidebar, ApiActivity, LanguageSwitcher, TranslatePipe],
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  private readonly healthService = inject(HealthService);
  private readonly signOutService = inject(SignOutService);
  protected readonly authSession = inject(AuthSessionService);

  /** The admin areas this user's permissions open; empty for a candidate, so the nav shows no admin links to them. */
  protected readonly adminSections = computed(() =>
    ADMIN_SECTIONS.filter((section) => this.authSession.hasPermission(section.permission)),
  );

  protected readonly health = toSignal(this.healthService.checkHealth(), {
    initialValue: { state: 'checking' as const, reason: null },
  });

  protected logout(): void {
    this.signOutService.signOut();
  }
}
