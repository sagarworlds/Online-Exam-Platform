import { Component, Signal, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink, isActive } from '@angular/router';
import { AdminSection, AdminSectionGroup } from '../../auth/admin-sections';
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

  /** The groups to draw, already filtered to what the signed-in user may open. */
  readonly groups = input.required<readonly AdminSectionGroup[]>();

  protected readonly collapsed = signal(this.readStoredCollapsed());

  /** The groups as drawn: each with the id that lets its heading name it, and each link with whether it is the current one. */
  protected readonly menu = computed<SidebarGroup[]>(() =>
    this.groups().map((group, index) => ({
      heading: group.heading,
      headingId: `admin-sidebar-heading-${index}`,
      links: group.sections.map((section) => ({ section, current: this.isCurrent(section) })),
    })),
  );

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

  private readStoredCollapsed(): boolean {
    try {
      return localStorage.getItem(COLLAPSED_STORAGE_KEY) === '1';
    } catch {
      return false;
    }
  }
}
