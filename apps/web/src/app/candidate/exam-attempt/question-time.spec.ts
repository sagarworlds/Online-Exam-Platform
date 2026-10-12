import { describe, expect, it } from 'vitest';
import { MAX_COUNTED_GAP_MS, QuestionTimeTracker } from './question-time';

/** Reads once a second from `fromMs` to `toMs`, as the page's tick does, with the question on screen and the reading flag given. */
function readFor(tracker: QuestionTimeTracker, questionId: string | null, fromMs: number, toMs: number, reading = true): void {
  for (let at = fromMs; at <= toMs; at += 1_000) {
    tracker.advance(questionId, at, reading);
  }
}

describe('QuestionTimeTracker', () => {
  it('counts the time since the last reading toward the question on screen', () => {
    const tracker = new QuestionTimeTracker();
    tracker.advance('q1', 0, true);

    readFor(tracker, 'q1', 1_000, 30_000);

    expect(tracker.pending()).toEqual([{ questionId: 'q1', seconds: 30 }]);
  });

  it('gives each second to the question that was on screen when it passed', () => {
    const tracker = new QuestionTimeTracker();
    tracker.advance('q1', 0, true);
    readFor(tracker, 'q1', 1_000, 10_000);

    // The candidate moves to q2 at 10 seconds; what passes from here on is q2's.
    readFor(tracker, 'q2', 11_000, 15_000);

    expect(tracker.pending()).toEqual([
      { questionId: 'q1', seconds: 10 },
      { questionId: 'q2', seconds: 5 },
    ]);
  });

  it('counts nothing while the candidate is not reading, such as when paused or with the page hidden', () => {
    const tracker = new QuestionTimeTracker();
    tracker.advance('q1', 0, true);
    readFor(tracker, 'q1', 1_000, 20_000, false);

    readFor(tracker, 'q1', 21_000, 25_000);

    expect(tracker.pending()).toEqual([{ questionId: 'q1', seconds: 5 }]);
  });

  it('counts nothing when no question is on screen', () => {
    const tracker = new QuestionTimeTracker();
    tracker.advance(null, 0, true);

    readFor(tracker, null, 1_000, 9_000);

    expect(tracker.pending()).toEqual([]);
  });

  it('does not count a gap longer than the largest counted one, such as a sleeping device', () => {
    const tracker = new QuestionTimeTracker();
    tracker.advance('q1', 0, true);

    tracker.advance('q1', 10 * 60_000, true);

    expect(tracker.pending()).toEqual([{ questionId: 'q1', seconds: MAX_COUNTED_GAP_MS / 1000 }]);
  });

  it('reports only what has grown since it was last reported, so a repeat sends nothing new', () => {
    const tracker = new QuestionTimeTracker();
    tracker.advance('q1', 0, true);
    readFor(tracker, 'q1', 1_000, 12_000);

    tracker.markReported('q1', 12);
    expect(tracker.pending()).toEqual([]);

    readFor(tracker, 'q1', 13_000, 15_000);
    expect(tracker.pending()).toEqual([{ questionId: 'q1', seconds: 15 }]);
  });

  it('starts a question from what the server holds, so a reload does not start its count from zero', () => {
    const tracker = new QuestionTimeTracker();
    tracker.seed('q1', 100);
    tracker.advance('q1', 0, true);

    readFor(tracker, 'q1', 1_000, 20_000);

    // 100 held plus 20 counted since the reload: the total to report is 120, and what the server held is not reported back as new.
    expect(tracker.pending()).toEqual([{ questionId: 'q1', seconds: 120 }]);
  });

  it('never reports a seeded total again until it grows', () => {
    const tracker = new QuestionTimeTracker();
    tracker.seed('q1', 100);

    expect(tracker.pending()).toEqual([]);
  });

  it('keeps the highest total a seed was given, whatever order the seeds come in', () => {
    const tracker = new QuestionTimeTracker();
    tracker.seed('q1', 100);
    tracker.seed('q1', 40);

    tracker.advance('q1', 0, true);
    tracker.advance('q1', 1_000, true);

    expect(tracker.pending()).toEqual([{ questionId: 'q1', seconds: 101 }]);
  });

  it('reports whole seconds only, so a fraction waits for the next whole second', () => {
    const tracker = new QuestionTimeTracker();
    tracker.advance('q1', 0, true);

    tracker.advance('q1', 900, true);

    expect(tracker.pending()).toEqual([]);
  });
});
