import { Component, computed, inject } from '@angular/core';
import { openAdminGroups } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { AdminAreaList } from '../admin-area-list/admin-area-list';

/**
 * The admin landing page: a row for every admin area the signed-in user's permissions open, under the same headings the sidebar uses.
 * A heading shows only when the user may open something under it.
 */
@Component({
  selector: 'app-admin-home',
  imports: [AdminAreaList, TranslatePipe],
  templateUrl: './admin-home.html',
  styleUrl: './admin-home.css',
})
export class AdminHome {
  private readonly authSession = inject(AuthSessionService);

  protected readonly groups = computed(() => openAdminGroups(this.authSession));
}
