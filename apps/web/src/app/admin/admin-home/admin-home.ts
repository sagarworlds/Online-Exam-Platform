import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ADMIN_SECTIONS } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { AdminIcon } from '../../shared/admin-icon/admin-icon';

/** The admin landing page: a row for every admin area the signed-in user's permissions open, with the icon the sidebar uses for it. */
@Component({
  selector: 'app-admin-home',
  imports: [RouterLink, AdminIcon, TranslatePipe],
  templateUrl: './admin-home.html',
  styleUrl: './admin-home.css',
})
export class AdminHome {
  private readonly authSession = inject(AuthSessionService);

  protected readonly sections = computed(() =>
    ADMIN_SECTIONS.filter((section) => this.authSession.hasPermission(section.permission)),
  );
}
