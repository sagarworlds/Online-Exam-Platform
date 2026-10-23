import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

/** Follow the system, or one theme chosen by hand. */
export type ThemeChoice = 'system' | 'light' | 'dark';

export const THEME_CHOICES: readonly ThemeChoice[] = ['system', 'light', 'dark'];

/** Where the choice is kept; `public/theme-init.js` reads the same key before the page is drawn. */
export const THEME_STORAGE_KEY = 'oep.theme';

/** The colour of the browser's toolbar and the installed app's title bar for each theme; the same as the two `theme-color` tags in index.html. */
const TOOLBAR_COLOUR = { light: '#1a56db', dark: '#0e1320' } as const;

export function isThemeChoice(value: unknown): value is ThemeChoice {
  return value === 'system' || value === 'light' || value === 'dark';
}

/**
 * The page's theme: the system's light or dark by default, or one the person picks (WCAG leaves it to them; some read better in one).
 * A hand-picked theme is the `data-theme` attribute on the page, which tokens.css answers; "system" removes it, so the page follows the
 * system again, live. The choice is kept in this browser only (local storage, in a try block: a blocked store only means it is not
 * remembered), and `theme-init.js` applies it before the first paint.
 *
 * The toolbar colour follows too: the two `theme-color` tags (one for each system scheme) are both set to the chosen theme's colour,
 * and put back for "system".
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  readonly choice = signal<ThemeChoice>(this.stored());

  constructor() {
    this.apply(this.choice());
  }

  set(choice: ThemeChoice): void {
    this.choice.set(choice);
    this.apply(choice);
    try {
      if (choice === 'system') {
        localStorage.removeItem(THEME_STORAGE_KEY);
      } else {
        localStorage.setItem(THEME_STORAGE_KEY, choice);
      }
    } catch {
      // Not remembered; the page still has the theme for this visit.
    }
  }

  private stored(): ThemeChoice {
    try {
      const value = localStorage.getItem(THEME_STORAGE_KEY);
      return isThemeChoice(value) ? value : 'system';
    } catch {
      return 'system';
    }
  }

  private apply(choice: ThemeChoice): void {
    const root = this.document.documentElement;
    if (choice === 'system') {
      root.removeAttribute('data-theme');
    } else {
      root.setAttribute('data-theme', choice);
    }

    for (const tag of Array.from(this.document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]'))) {
      const own = tag.media.includes('dark') ? TOOLBAR_COLOUR.dark : TOOLBAR_COLOUR.light;
      tag.content = choice === 'system' ? own : TOOLBAR_COLOUR[choice];
    }
  }
}
