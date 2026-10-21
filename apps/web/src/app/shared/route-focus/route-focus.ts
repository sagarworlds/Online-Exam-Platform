import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, Injector, afterNextRender, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

/** The part of a URL that names the page: a change of only the query or the fragment is the same page. */
const pageOf = (url: string): string => url.split(/[?#]/)[0];

/**
 * Moves the keyboard focus and a screen reader's place to the new page's heading when the visitor goes to another page (WCAG 2.4.3).
 * The router swaps the page inside one document, so without this the focus is left on the link that was used, or on nothing once that
 * link is gone, and a screen reader says nothing at all about the new page.
 *
 * It leaves the focus alone for the first page the app opens (the visitor did not ask to move), for a change of only the query or fragment,
 * while a dialog is open (the dialog has the focus), and when the page has already put the focus somewhere inside itself.
 */
@Injectable({ providedIn: 'root' })
export class RouteFocus {
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private page: string | null = null;

  /** Starts following the router. Called once, by the root component. */
  start(): void {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((event) => {
        const page = pageOf(event.urlAfterRedirects);
        const moved = this.page !== null && page !== this.page;
        this.page = page;
        if (moved) {
          // After the new page has been drawn, so its heading exists.
          afterNextRender(() => this.focusHeading(), { injector: this.injector });
        }
      });
  }

  private focusHeading(): void {
    const main = this.document.getElementById('main-content');
    if (main === null || this.document.querySelector('dialog[open]') !== null) {
      return;
    }
    if (main.contains(this.document.activeElement)) {
      return;
    }

    const heading = main.querySelector<HTMLElement>('h1');
    if (heading !== null && !heading.hasAttribute('tabindex')) {
      // A heading takes the focus only through a script, so it is not in the tab order.
      heading.setAttribute('tabindex', '-1');
    }
    (heading ?? main).focus();
  }
}
