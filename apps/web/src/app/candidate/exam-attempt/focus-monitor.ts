import { FocusViolationKind } from '../candidate.models';

/**
 * Notices when the candidate leaves the exam page while an exam that watches for it is open (FR-22): the tab is hidden, the window
 * loses focus, or full screen is left. It only reports; the exam's limit, the count and what happens at the limit belong to the server.
 *
 * Switching tab fires both "hidden" and "blur", and returning fires both "visible" and "focus". One trip away is one departure, so
 * only the first signal of a trip is reported and the next one is allowed once the candidate is back. Leaving full screen is its
 * own departure, and only counts once the candidate has been in full screen: a candidate who never entered it has not left it.
 */
export class FocusMonitor {
  private started = false;
  /** True from the first signal of a trip away until the candidate is back, so the second signal of the same trip is ignored. */
  private away = false;
  private wasFullscreen = false;

  /**
   * @param document The page's document, to listen on and to ask about full screen.
   * @param onDeparture Called once per departure, with how the candidate left.
   */
  constructor(
    private readonly document: Document,
    private readonly onDeparture: (kind: FocusViolationKind) => void,
  ) {}

  /** Whether the browser lets this page ask for full screen. False where it is unsupported or switched off, so the page can skip the offer. */
  get canEnterFullscreen(): boolean {
    return this.document.fullscreenEnabled === true;
  }

  /** Whether the page is full screen right now. */
  get isFullscreen(): boolean {
    return this.document.fullscreenElement != null;
  }

  private readonly onVisibilityChange = (): void => {
    if (this.document.visibilityState === 'hidden') {
      this.leave('TabHidden');
    } else {
      this.back();
    }
  };

  private readonly onBlur = (): void => this.leave('WindowBlurred');

  // Returning to the page fires focus; a hidden tab that is shown again also fires visibilitychange. Either one ends the trip,
  // but a focus event while the tab is still hidden (some browsers fire one on the way out) must not.
  private readonly onFocus = (): void => {
    if (this.document.visibilityState !== 'hidden') {
      this.back();
    }
  };

  private readonly onFullscreenChange = (): void => {
    const now = this.isFullscreen;
    if (this.wasFullscreen && !now) {
      this.onDeparture('FullscreenExited');
    }
    this.wasFullscreen = now;
  };

  private leave(kind: FocusViolationKind): void {
    if (this.away) {
      return;
    }
    this.away = true;
    this.onDeparture(kind);
  }

  private back(): void {
    this.away = false;
  }

  /** Asks the browser for full screen. Must run from a click or key press; a refusal is ignored, since the exam goes on without it. */
  async enterFullscreen(): Promise<void> {
    try {
      await this.document.documentElement.requestFullscreen();
    } catch {
      // Refused (no gesture, or the browser forbids it): the other departures are still watched.
    }
  }

  /** Starts watching. Safe to call twice. */
  start(): void {
    if (this.started) {
      return;
    }

    this.started = true;
    this.away = false;
    this.wasFullscreen = this.isFullscreen;
    const doc = this.document;
    doc.addEventListener('visibilitychange', this.onVisibilityChange);
    doc.addEventListener('fullscreenchange', this.onFullscreenChange);
    doc.defaultView?.addEventListener('blur', this.onBlur);
    doc.defaultView?.addEventListener('focus', this.onFocus);
  }

  /** Stops watching. Safe to call when not started. */
  stop(): void {
    if (!this.started) {
      return;
    }

    this.started = false;
    const doc = this.document;
    doc.removeEventListener('visibilitychange', this.onVisibilityChange);
    doc.removeEventListener('fullscreenchange', this.onFullscreenChange);
    doc.defaultView?.removeEventListener('blur', this.onBlur);
    doc.defaultView?.removeEventListener('focus', this.onFocus);
  }
}
