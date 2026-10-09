import { Injectable, signal } from '@angular/core';

/**
 * Tracks the API calls in flight so the page can say, calmly, that it is waiting on the server.
 *
 * The aim is reassurance without noise. A call that finishes quickly shows nothing at all (so a fast
 * connection never sees a flicker), an indicator that has appeared stays long enough to be read as one
 * steady thing, and a longer wait adds a short explanation, since a cold or distant server can take a
 * few seconds to answer and a page that seems frozen makes people click again.
 */
@Injectable({ providedIn: 'root' })
export class ApiActivityService {
  /** Calls finishing within this long never show an indicator. */
  static readonly SHOW_AFTER_MS = 300;

  /** Once shown, the indicator stays at least this long, so it does not blink on and off. */
  static readonly MIN_VISIBLE_MS = 600;

  /** A continuous wait this long adds the "still working" explanation. */
  static readonly SLOW_AFTER_MS = 5000;

  private readonly visibleState = signal(false);
  private readonly slowState = signal(false);

  /** True while the thin progress indicator should be on screen. */
  readonly visible = this.visibleState.asReadonly();

  /** True while a wait has gone on long enough to explain it. */
  readonly slow = this.slowState.asReadonly();

  private inFlight = 0;
  private shownAt = 0;
  private showTimer: ReturnType<typeof setTimeout> | undefined;
  private hideTimer: ReturnType<typeof setTimeout> | undefined;
  private slowTimer: ReturnType<typeof setTimeout> | undefined;

  /** Notes that a call has started. Pair every call with one {@link end}. */
  begin(): void {
    this.inFlight++;
    if (this.inFlight > 1) {
      return;
    }

    // Busy again before a pending hide: carry on showing the same indicator.
    clearTimeout(this.hideTimer);
    if (!this.visibleState()) {
      this.showTimer = setTimeout(() => {
        this.shownAt = Date.now();
        this.visibleState.set(true);
      }, ApiActivityService.SHOW_AFTER_MS);
    }

    this.slowTimer = setTimeout(() => this.slowState.set(true), ApiActivityService.SLOW_AFTER_MS);
  }

  /** Notes that a call has finished, however it ended (answered, failed or cancelled). */
  end(): void {
    if (this.inFlight === 0) {
      return;
    }

    this.inFlight--;
    if (this.inFlight > 0) {
      return;
    }

    clearTimeout(this.showTimer);
    clearTimeout(this.slowTimer);
    this.slowState.set(false);

    if (this.visibleState()) {
      const remaining = Math.max(0, ApiActivityService.MIN_VISIBLE_MS - (Date.now() - this.shownAt));
      this.hideTimer = setTimeout(() => this.visibleState.set(false), remaining);
    }
  }
}
