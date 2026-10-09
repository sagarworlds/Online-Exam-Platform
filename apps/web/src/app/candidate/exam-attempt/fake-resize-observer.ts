/**
 * A stand-in for ResizeObserver, which jsdom does not provide. Each observer keeps its callback, so a spec can report that the
 * element it observes was resized, the way the browser does after a layout change (see ExamAttempt, which follows its pinned block).
 */
export class FakeResizeObserver {
  /** Every observer created since the list was last emptied, so a spec can report the resize of each one. */
  static instances: FakeResizeObserver[] = [];

  constructor(private readonly callback: () => void) {
    FakeResizeObserver.instances.push(this);
  }

  observe(): void {
    // jsdom has no layout to observe, so nothing is watched; a spec reports resizes with report().
  }

  disconnect(): void {
    // Nothing is watched, so there is nothing to stop.
  }

  /** Reports a resize of the observed element. */
  report(): void {
    this.callback();
  }
}
