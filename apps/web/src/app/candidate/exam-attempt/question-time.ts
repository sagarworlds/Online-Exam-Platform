/**
 * The longest gap between two readings of the clock that still counts as reading. A longer gap is the device asleep or the page frozen, not a
 * candidate looking at a question, so it is not counted.
 */
export const MAX_COUNTED_GAP_MS = 5_000;

/**
 * Measures how long a candidate has each question on screen, for the time-per-question analytics (FR-36).
 *
 * Each question has a running total, in seconds, that only grows. The page reports the whole total, never the time since the last report, so
 * a report that is repeated or arrives late cannot count the same seconds twice, and the server keeps the highest total it has been given. A
 * total is started from what the server already holds (see `seed`), so a reload carries on from it rather than from zero.
 */
export class QuestionTimeTracker {
  private readonly totals = new Map<string, number>();
  private readonly reported = new Map<string, number>();
  private lastMs: number | null = null;

  /**
   * Counts the time since the last call toward the question on screen, when the candidate is reading it.
   *
   * @param questionId The question on screen, or null when none is.
   * @param nowMs The current time, in milliseconds.
   * @param reading Whether the candidate can be reading: the attempt is running, not paused, and the page is in view. Time that is not reading
   *   is passed over, so a pause or a hidden tab does not count.
   */
  advance(questionId: string | null, nowMs: number, reading: boolean): void {
    const since = this.lastMs;
    this.lastMs = nowMs;
    if (since === null || questionId === null || !reading) {
      return;
    }

    const gapMs = Math.min(nowMs - since, MAX_COUNTED_GAP_MS);
    if (gapMs > 0) {
      this.totals.set(questionId, (this.totals.get(questionId) ?? 0) + gapMs / 1000);
    }
  }

  /**
   * Starts a question's total from the time the server already holds for it, so a reload, or another device, carries on from it.
   *
   * @param questionId The question.
   * @param seconds The running total the server holds, in whole seconds. What the server holds is never reported back to it.
   */
  seed(questionId: string, seconds: number): void {
    const held = Math.floor(seconds);
    this.totals.set(questionId, Math.max(this.totals.get(questionId) ?? 0, held));
    this.reported.set(questionId, Math.max(this.reported.get(questionId) ?? 0, held));
  }

  /**
   * The questions whose total has grown since it was last reported, each with the whole-second total to send.
   *
   * @returns The totals to report; empty when nothing has grown.
   */
  pending(): { questionId: string; seconds: number }[] {
    const grown: { questionId: string; seconds: number }[] = [];
    for (const [questionId, total] of this.totals) {
      const seconds = Math.floor(total);
      if (seconds > (this.reported.get(questionId) ?? 0)) {
        grown.push({ questionId, seconds });
      }
    }
    return grown;
  }

  /**
   * Records that the server took a total, so it is not sent again until the total grows.
   *
   * @param questionId The question.
   * @param seconds The total the server took.
   */
  markReported(questionId: string, seconds: number): void {
    this.reported.set(questionId, Math.max(this.reported.get(questionId) ?? 0, seconds));
  }
}
