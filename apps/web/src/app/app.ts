import { Component, computed, effect, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { openAdminGroups } from './auth/admin-sections';
import { AuthSessionService } from './auth/auth-session.service';
import { BrandingService } from './branding/branding.service';
import { SignOutService } from './auth/sign-out.service';
import { HealthService } from './health/health.service';
import { NotificationsStateService } from './notifications/notifications-state.service';
import { AdminSidebar } from './shared/admin-sidebar/admin-sidebar';
import { LanguageSwitcher } from './i18n/language-switcher';
import { TranslatePipe } from './i18n/translate.pipe';
import { ApiActivity } from './shared/api-activity/api-activity';
import { ExamMode } from './shared/exam-mode/exam-mode';
import { RouteFocus } from './shared/route-focus/route-focus';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, AdminSidebar, ApiActivity, LanguageSwitcher, TranslatePipe],
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  private readonly healthService = inject(HealthService);
  private readonly signOutService = inject(SignOutService);
  private readonly router = inject(Router);
  protected readonly authSession = inject(AuthSessionService);

  /** Whether an exam is open on screen, in which case the header is put down to the institute's name and the language. */
  protected readonly examMode = inject(ExamMode);

  /** The institute's name and logo for the header (FR-41); the platform's own look while none is set. */
  protected readonly branding = inject(BrandingService);

  /** The unread count the header shows beside "Notifications" (FR-39); zero while nobody is signed in. */
  protected readonly notifications = inject(NotificationsStateService);

  constructor() {
    // A new page takes the focus (WCAG 2.4.3), so a keyboard or screen-reader user is not left on the link they used.
    inject(RouteFocus).start();

    // The count is read when someone signs in or out, and again on each page change, so a notice read or sent elsewhere shows up without a timer.
    effect(() => {
      if (this.authSession.isAuthenticated()) {
        this.notifications.refresh();
      } else {
        this.notifications.clear();
      }
    });

    this.router.events
      .pipe(filter((event) => event instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe(() => {
        if (this.authSession.isAuthenticated()) {
          this.notifications.refresh();
        }
      });
  }

  /** The admin areas this user's permissions open, in their groups; empty for a candidate, so the nav shows no admin links to them. */
  protected readonly adminGroups = computed(() => openAdminGroups(this.authSession));

  protected readonly health = toSignal(this.healthService.checkHealth(), {
    initialValue: { state: 'checking' as const, reason: null },
  });

  protected logout(): void {
    this.signOutService.signOut();
  }
}
