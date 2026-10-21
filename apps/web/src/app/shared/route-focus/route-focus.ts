import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, Injector, afterNextRender, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Route, Router } from '@angular/router';
import { filter } from 'rxjs';

/**
 * Moves the keyboard focus and a screen reader's place to the new page's heading when the visitor goes to another page (WCAG 2.4.3).
 * The router swaps the page inside one document, so without this the focus is left on the link that was used, or on nothing once that
 * link is gone, and a screen reader says nothing at all about the new page.
 *
 * A page is a top-level route of app.routes.ts, with its route parameters. It leaves the focus alone for the first page the app opens (the
 * visitor did not ask to move), for a change of only the query or fragment, for a change inside a page (the question bank's side panel
 * opening and closing is a child route, and the list under it must not jump), while a dialog is open (the dialog has the focus), and when the
 * page has already put the focus somewhere inside itself.
 */
@Injectable({ providedIn: 'root' })
export class RouteFocus {
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  private seenAPage = false;
  private route: Route | null = null;
  private params = '';

  /** Starts following the router. Called once, by the root component. */
  start(): void {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        const top = this.router.routerState.snapshot.root.firstChild;
        const route = top?.routeConfig ?? null;
        const params = JSON.stringify(top?.params ?? {});
        const moved = this.seenAPage && (route !== this.route || params !== this.params);

        this.seenAPage = true;
        this.route = route;
        this.params = params;
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
