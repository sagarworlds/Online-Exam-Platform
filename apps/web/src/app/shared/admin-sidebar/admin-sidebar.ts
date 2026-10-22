import { Component, Signal, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, isActive } from '@angular/router';
import { filter } from 'rxjs';
import type { AttentionState } from '../../admin/admin-attention/admin-attention.service';
import { AdminSection, AdminSectionGroup, AttentionKey } from '../../auth/admin-sections';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { AdminIcon } from '../admin-icon/admin-icon';

const COLLAPSED_STORAGE_KEY = 'exam-platform.admin-sidebar-collapsed';

/** One link of the sidebar: the section it opens, and whether the page the user is on is that section's own or one gathered under it. */
interface SidebarLink {
  section: AdminSection;
  current: Signal<boolean>;
}

/** One run of links as the sidebar draws it, with the id its heading has so the run can be named by it. */
interface SidebarGroup {
  heading: MessageKey | null;
  headingId: string;
  links: readonly SidebarLink[];
}

/**
 * The admin area's persistent left-hand navigation: the sections the caller passes in, in groups under small headings (already filtered to
 * what the signed-in user's permissions open — this component only renders them). A heading is plain text that names its group for a
 * screen reader, not a control, and a group with no heading (Settings) is drawn without one.
 * Collapsing swaps each link's label for its icon to save width, but never removes the link itself or its accessible name, so every
 * section stays one click away either way (FR: a collapsed menu keeps the same functionality as expanded). The headings are then hidden
 * from the eye only, and a rule between the groups stands in for them.
 */
@Component({
  selector: 'app-admin-sidebar',
  imports: [RouterLink, AdminIcon, TranslatePipe],
  templateUrl: './admin-sidebar.html',
  styleUrl: './admin-sidebar.css',
})
export class AdminSidebar {
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

  /** The groups to draw, already filtered to what the signed-in user may open. */
  readonly groups = input.required<readonly AdminSectionGroup[]>();

  /** How many things wait in each queue, for the badge on its entry; an entry whose count is not known, or is none, has no badge. */
  readonly attention = input<Readonly<Partial<Record<AttentionKey, AttentionState>>>>({});

  protected readonly collapsed = signal(this.readStoredCollapsed());

  /** Whether the menu is open on a phone, where it is one row until opened. It plays no part elsewhere. */
  protected readonly phoneOpen = signal(false);

  /** How many are waiting in all the queues the menu lists, for the row a phone shows it behind: the sum, and the same in words. Null when none. */
  protected readonly waiting = computed<{ text: string; words: string } | null>(() => {
    const counts = this.attention();
    let total = 0;
    let atLimit = false;
    for (const group of this.groups()) {
      for (const section of group.sections) {
        const state = section.attention ? counts[section.attention] : undefined;
        if (state?.status === 'ready') {
          total += state.count;
          atLimit ||= state.atLimit;
        }
      }
    }
    if (total === 0) {
      return null;
    }
    return {
      text: atLimit ? `${total}+` : String(total),
      words: this.i18n.t(atLimit ? 'admin.attention.badgeAtLimit' : 'admin.attention.badge', {
        count: total,
      }),
    };
  });

  /** The groups as drawn: each with the id that lets its heading name it, and each link with whether it is the current one. */
  protected readonly menu = computed<SidebarGroup[]>(() =>
    this.groups().map((group, index) => ({
      heading: group.heading,
      headingId: `admin-sidebar-heading-${index}`,
      links: group.sections.map((section) => ({ section, current: this.isCurrent(section) })),
    })),
  );

  constructor() {
    // Following a link is what the open menu was for: on a phone it folds away, so the page that was asked for is the one in view.
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.phoneOpen.set(false));
  }

  /** The badge on a section's entry: the number waiting, and the same in words for a screen reader. Null when nothing waits or it is not known. */
  protected badgeOf(section: AdminSection): { text: string; words: string } | null {
    const state = section.attention ? this.attention()[section.attention] : undefined;
    if (state?.status !== 'ready' || state.count === 0) {
      return null;
    }
    const text = state.atLimit ? `${state.count}+` : String(state.count);
    const words = this.i18n.t(
      state.atLimit ? 'admin.attention.badgeAtLimit' : 'admin.attention.badge',
      { count: state.count },
    );
    return { text, words };
  }

  /** What a link is called: its section, and how many are waiting in it. */
  protected nameOf(section: AdminSection): string {
    const name = this.i18n.t(section.label);
    const badge = this.badgeOf(section);
    return badge ? `${name}, ${badge.words}` : name;
  }

  protected toggle(): void {
    const next = !this.collapsed();
    this.collapsed.set(next);
    try {
      // Best-effort: a viewer who blocks storage, or a private window, still gets a working toggle
      // for the rest of the session, just not one that survives a reload.
      localStorage.setItem(COLLAPSED_STORAGE_KEY, next ? '1' : '0');
    } catch {
      /* Storage unavailable; the in-memory signal above still drives the UI for this session. */
    }
  }

  /**
   * Whether the user is on the section's page or below it (the exam editor is below Exams), or on one of the pages the section lists
   * (Settings is current on Branding), so the menu shows where in the admin area they are.
   */
  private isCurrent(section: AdminSection): Signal<boolean> {
    const paths = [section.path, ...(section.pages ?? []).map((page) => page.path)];
    const active = paths.map((path) => isActive(path, this.router));
    return computed(() => active.some((isOnPath) => isOnPath()));
  }

  /**
   * Whether the menu starts as a rail of icons: as the user last left it, and where they have not chosen, on a tablet's width or less, where
   * the page is worth more than twelve rems of labels.
   */
  private readStoredCollapsed(): boolean {
    try {
      const stored = localStorage.getItem(COLLAPSED_STORAGE_KEY);
      if (stored !== null) {
        return stored === '1';
      }
    } catch {
      // Blocked storage is the same as no choice.
    }
    return typeof matchMedia === 'function' && matchMedia('(max-width: 64rem)').matches;
  }
}
