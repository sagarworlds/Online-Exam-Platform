import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, InjectionToken, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SwUpdate } from '@angular/service-worker';
import { filter } from 'rxjs';

/** How often a page that stays open asks whether a newer version is out. */
export const UPDATE_CHECK_INTERVAL_MS = 10 * 60 * 1000;

/** The first look comes a little after start-up, so it never competes with the requests that draw the first screen. */
const FIRST_CHECK_DELAY_MS = 3_000;

/** Coming back to the tab, going online and the timer can fire together; one look in this long is enough. */
const MIN_GAP_BETWEEN_CHECKS_MS = 60_000;

/** The script the page starts from, in the form the build names it: its name carries a hash of its contents, so a new build has a new name. */
const MAIN_SCRIPT_IN_HTML = /<script[^>]*\ssrc=["'](main-[^"']+\.js)["']/i;

/** Reloads the page. A token so that a test can see the reload without the browser leaving the test page. */
export const RELOAD_PAGE = new InjectionToken<() => void>('RELOAD_PAGE', {
  factory: () => () => location.reload(),
});

/**
 * Finds out when the server holds a newer build than the one running in this tab, and moves the tab onto it when the person says so.
 *
 * The service worker keeps the app shell for offline use, and left to itself it moves a tab onto a new build only on a reload after it
 * has downloaded the build, which often takes more than one reload and never tells anyone. Here the question is asked of the server
 * itself, past the worker: the build's start script is named by a hash of its contents, so the running tab and the server's
 * `index.html` name different scripts exactly when a new build is out. That works even when the worker is stale or stuck, which is when
 * a plain reload does not help.
 *
 * Updating drops the worker and everything it stored, and reloads. That is what clearing the site's data does by hand. The sign-in and
 * everything the app keeps in the browser's storage stay; the worker's caches hold only the app's own files (see app.config.ts).
 */
@Injectable({ providedIn: 'root' })
export class AppUpdateService {
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  /** Optional: it is not provided where there is no worker (the tests, and the development server). */
  private readonly swUpdate = inject(SwUpdate, { optional: true });
  private readonly reloadPage = inject(RELOAD_PAGE);

  /** The start script this tab was built from; null when it cannot be told (the development server names it plainly), and then nothing is checked. */
  private readonly running = this.document.querySelector('script[src^="main-"]')?.getAttribute('src') ?? null;
  private lastCheckAt = 0;
  private started = false;

  /** Whether a newer build than this tab's is on the server. It stays true, for a tab only gets older, and is true again for the next build after a reload. */
  readonly updateAvailable = signal(false);

  /** Whether an update is under way, so the button cannot be pressed twice. */
  readonly applying = signal(false);

  /** Starts looking: soon after start-up, on a timer, when the tab comes back to the front and when the network returns. */
  start(): void {
    const view = this.document.defaultView;
    if (this.started || view === null) {
      return;
    }
    this.started = true;

    // The worker's own word, when it has one: a new version is downloaded, or it can no longer serve this tab.
    if (this.swUpdate?.isEnabled) {
      this.swUpdate.versionUpdates
        .pipe(filter((event) => event.type === 'VERSION_READY'), takeUntilDestroyed(this.destroyRef))
        .subscribe(() => this.updateAvailable.set(true));
      this.swUpdate.unrecoverable.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.updateAvailable.set(true));
    }

    const look = () => void this.check();
    const whenShown = () => {
      if (this.document.visibilityState === 'visible') {
        look();
      }
    };
    const first = view.setTimeout(look, FIRST_CHECK_DELAY_MS);
    const every = view.setInterval(look, UPDATE_CHECK_INTERVAL_MS);
    this.document.addEventListener('visibilitychange', whenShown);
    view.addEventListener('online', look);
    this.destroyRef.onDestroy(() => {
      view.clearTimeout(first);
      view.clearInterval(every);
      this.document.removeEventListener('visibilitychange', whenShown);
      view.removeEventListener('online', look);
    });
  }

  /** Asks the server which build is current, and flags an update when it is not the one running. Never throws: a page offline just asks again later. */
  async check(): Promise<void> {
    const now = Date.now();
    if (this.updateAvailable() || this.running === null || now - this.lastCheckAt < MIN_GAP_BETWEEN_CHECKS_MS) {
      return;
    }
    this.lastCheckAt = now;

    // Lets the worker look for its own update too, so that its files are already in when the page reloads.
    this.swUpdate?.checkForUpdate().catch(() => undefined);

    const latest = await this.latestMainScript();
    if (latest !== null && latest !== this.running) {
      this.updateAvailable.set(true);
    }
  }

  /** Drops the worker and what it stored, then reloads, so that the page comes from the server. */
  async apply(): Promise<void> {
    if (this.applying()) {
      return;
    }
    this.applying.set(true);
    try {
      await this.dropCachedCopies();
    } catch {
      // Whatever could not be dropped, the reload is still the best thing left to do.
    }
    this.reloadPage();
  }

  /** The start script named by the server's own `index.html`, read past the worker and the browser's cache; null when it cannot be read. */
  private async latestMainScript(): Promise<string | null> {
    try {
      const url = new URL('index.html', this.document.baseURI);
      // `ngsw-bypass` makes the worker pass the request on; the stamp keeps a cache in between from answering for the server.
      url.searchParams.set('ngsw-bypass', 'true');
      url.searchParams.set('_', String(Date.now()));
      const response = await fetch(url, { cache: 'no-store', headers: { 'ngsw-bypass': 'true' } });
      return response.ok ? (MAIN_SCRIPT_IN_HTML.exec(await response.text())?.[1] ?? null) : null;
    } catch {
      return null;
    }
  }

  private async dropCachedCopies(): Promise<void> {
    const view = this.document.defaultView;
    const registrations = (await view?.navigator.serviceWorker?.getRegistrations()) ?? [];
    await Promise.all(registrations.map((registration) => registration.unregister()));
    const names = (await view?.caches?.keys()) ?? [];
    await Promise.all(names.map((name) => view?.caches.delete(name)));
  }
}
