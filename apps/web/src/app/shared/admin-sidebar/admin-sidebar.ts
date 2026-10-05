import { Component, input, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AdminSection } from '../../auth/admin-sections';
import { AdminIcon } from '../admin-icon/admin-icon';

const COLLAPSED_STORAGE_KEY = 'exam-platform.admin-sidebar-collapsed';

/**
 * The admin area's persistent left-hand navigation: one link per admin section the caller passes in
 * (already filtered to what the signed-in user's permissions open — this component only renders them).
 * Collapsing swaps each link's label for its icon to save width, but never removes the link itself or
 * its accessible name, so every section stays one click away either way (FR: a collapsed menu keeps the
 * same functionality as expanded).
 */
@Component({
  selector: 'app-admin-sidebar',
  imports: [RouterLink, RouterLinkActive, AdminIcon],
  templateUrl: './admin-sidebar.html',
  styleUrl: './admin-sidebar.css',
})
export class AdminSidebar {
  readonly sections = input.required<readonly AdminSection[]>();

  protected readonly collapsed = signal(this.readStoredCollapsed());

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

  private readStoredCollapsed(): boolean {
    try {
      return localStorage.getItem(COLLAPSED_STORAGE_KEY) === '1';
    } catch {
      return false;
    }
  }
}
