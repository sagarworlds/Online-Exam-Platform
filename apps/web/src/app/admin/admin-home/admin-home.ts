import { Component, OnInit, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AttentionKey, openAdminGroups } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { RefreshButton } from '../../shared/refresh-button/refresh-button';
import { AdminAreaList } from '../admin-area-list/admin-area-list';
import { AdminAttentionService } from '../admin-attention/admin-attention.service';

/** One tile of "Needs attention": what it counts, in words, and the page that deals with it. */
interface TileDefinition {
  key: AttentionKey;
  label: MessageKey;
  detail: MessageKey;
  path: string;
}

const TILES: readonly TileDefinition[] = [
  { key: 'attemptRequests', label: 'admin.area.attemptRequests.name', detail: 'admin.attention.attemptRequests.detail', path: '/admin/attempt-requests' },
  { key: 'disputes', label: 'admin.area.disputes.name', detail: 'admin.attention.disputes.detail', path: '/admin/disputes' },
  { key: 'issues', label: 'admin.area.issueReports.name', detail: 'admin.attention.issues.detail', path: '/admin/issue-reports' },
  { key: 'liveExams', label: 'admin.attention.liveExams', detail: 'admin.attention.liveExams.detail', path: '/exams' },
];

/** What a tile shows, ready to draw. */
interface Tile {
  key: AttentionKey;
  path: string;
  label: string;
  /** The number, or a mark while it is not known. */
  figure: string;
  /** What the figure means, in words: "waiting for a decision", "Nothing waiting", "Checking…". */
  detail: string;
  /** Whether something is waiting, which is what draws the eye. */
  waiting: boolean;
  /** The whole tile as one sentence, which is how a screen reader reads the link. */
  name: string;
}

/**
 * The admin landing page: first what needs staff (attempt requests to decide, disputes and reported problems to answer, exams running), as
 * tiles that open the page that deals with each, then a row for every admin area the signed-in user's permissions open, under the same
 * headings the sidebar uses. A heading shows only when the user may open something under it, and so does a tile.
 */
@Component({
  selector: 'app-admin-home',
  imports: [AdminAreaList, RefreshButton, RouterLink, TranslatePipe],
  templateUrl: './admin-home.html',
  styleUrl: './admin-home.css',
})
export class AdminHome implements OnInit {
  private readonly authSession = inject(AuthSessionService);
  private readonly i18n = inject(I18nService);
  protected readonly attention = inject(AdminAttentionService);

  protected readonly groups = computed(() => openAdminGroups(this.authSession));

  /** The tiles the user may have, with what is known of each count. A tile whose queue the user may not read is not drawn. */
  protected readonly tiles = computed<Tile[]>(() => {
    const counts = this.attention.counts();
    const tiles: Tile[] = [];
    for (const definition of TILES) {
      const state = counts[definition.key];
      if (state === undefined) {
        continue;
      }

      const label = this.i18n.t(definition.label);
      let figure: string;
      let detail: string;
      let waiting = false;
      if (state.status === 'ready') {
        figure = state.atLimit ? `${state.count}+` : String(state.count);
        waiting = state.count > 0;
        detail = waiting ? this.i18n.t(definition.detail) : this.i18n.t('admin.attention.clear');
      } else {
        figure = state.status === 'loading' ? '…' : '–';
        detail = this.i18n.t(state.status === 'loading' ? 'admin.attention.loading' : 'admin.attention.failed');
      }

      // Only punctuation joins the parts, which reads the same in every language, so it is not a message of its own.
      const name = state.status === 'ready' && waiting ? `${label}: ${figure} ${detail}` : `${label}: ${detail}`;
      tiles.push({ key: definition.key, path: definition.path, label, figure, detail, waiting, name });
    }
    return tiles;
  });

  ngOnInit(): void {
    // The page is where staff look to see what needs them, so it asks again rather than showing what the sidebar read a while ago.
    this.attention.refresh();
  }
}
