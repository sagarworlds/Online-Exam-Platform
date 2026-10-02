import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ADMIN_SECTIONS } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';

/** The admin landing page: a card for every admin area the signed-in user's permissions open. */
@Component({
  selector: 'app-admin-home',
  imports: [RouterLink],
  templateUrl: './admin-home.html',
})
export class AdminHome {
  private readonly authSession = inject(AuthSessionService);

  protected readonly sections = computed(() =>
    ADMIN_SECTIONS.filter((section) => this.authSession.hasPermission(section.permission)),
  );
}
