import { Component, computed, inject } from '@angular/core';
import { SETTINGS_PAGES, openSections } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { AdminAreaList } from '../admin-area-list/admin-area-list';

/**
 * The Settings page: a row for each page needed only now and then (branding, instruction templates, the WhatsApp test, candidate codes)
 * that the signed-in user may open. The route lets in anyone who may open at least one of them, so the list is never empty.
 */
@Component({
  selector: 'app-settings',
  imports: [AdminAreaList, TranslatePipe],
  templateUrl: './settings.html',
})
export class Settings {
  private readonly authSession = inject(AuthSessionService);

  protected readonly pages = computed(() => openSections(SETTINGS_PAGES, this.authSession));
}
