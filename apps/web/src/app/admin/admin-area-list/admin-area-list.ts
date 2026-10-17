import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminSection } from '../../auth/admin-sections';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { AdminIcon } from '../../shared/admin-icon/admin-icon';

/**
 * A list of admin areas as whole-row links, each with the icon the sidebar uses for it, so a tap anywhere on a row opens the area. The
 * admin home page and the Settings page both show their areas this way, so the rows look and behave the same on both.
 */
@Component({
  selector: 'app-admin-area-list',
  imports: [RouterLink, AdminIcon, TranslatePipe],
  templateUrl: './admin-area-list.html',
  styleUrl: './admin-area-list.css',
})
export class AdminAreaList {
  /** The areas to list, already filtered to what the signed-in user may open. */
  readonly sections = input.required<readonly AdminSection[]>();
}
