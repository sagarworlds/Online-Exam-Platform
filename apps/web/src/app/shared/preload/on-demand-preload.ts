import { Injectable } from '@angular/core';
import { PreloadingStrategy, Route } from '@angular/router';
import { Observable, of } from 'rxjs';

/** What a route names in its `data.preloadOn`, and what a page asks for when it knows the route is likely next. */
export type PreloadKey = 'exam';

/**
 * Loads a route's code ahead of time, but only when a page that knows it is coming asks. The exam page is the case: it is the heaviest
 * screen a candidate opens, and the instructions page before it is where they have already chosen to go there, so its code can arrive while
 * the candidate reads, and not after Start is pressed with the clock running. Nothing is loaded for anyone who does not get that far, which is
 * why this is not "preload everything": a screen a person may never open is not worth their connection.
 *
 * It never leaves a request waiting in the router's preloader, which would hold up every later preload behind it.
 */
@Injectable({ providedIn: 'root' })
export class OnDemandPreload implements PreloadingStrategy {
  /** The routes that name a key and have not been loaded yet, with how to load them. The router offers each again after every navigation. */
  private readonly waiting = new Map<Route, () => Observable<unknown>>();
  private readonly asked = new Set<string>();

  /** Called by the router for each route that loads lazily. */
  preload(route: Route, load: () => Observable<unknown>): Observable<unknown> {
    const key: unknown = route.data?.['preloadOn'];
    if (typeof key !== 'string') {
      return of(null);
    }

    // Asked for before the router offered the route: load it now.
    if (this.asked.has(key)) {
      return load();
    }

    this.waiting.set(route, load);
    return of(null);
  }

  /**
   * Starts loading every route that names the key. Asking again does nothing more, and a load that fails is not a problem: the navigation
   * that needs the route asks for it again.
   */
  request(key: PreloadKey): void {
    this.asked.add(key);
    for (const [route, load] of this.waiting) {
      if (route.data?.['preloadOn'] === key) {
        this.waiting.delete(route);
        load().subscribe({ error: () => undefined });
      }
    }
  }
}
