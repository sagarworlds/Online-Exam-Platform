import { HttpErrorResponse } from '@angular/common/http';
import { Subject } from 'rxjs';
import { afterEach, beforeEach, vi } from 'vitest';
import { AnswerSync, AnswerSyncApi, PendingAnswer, RETRY_DELAYS_MS, isRefusal } from './answer-sync';

interface Call {
  kind: 'save' | 'saveSet' | 'clear';
  questionId: string;
  optionIds: string[];
  reply: Subject<void>;
}

/** A fake API whose calls wait until the test answers them, so the order and the failures are the test's to choose. */
class FakeApi implements AnswerSyncApi {
  readonly calls: Call[] = [];

  saveAnswer(_attemptId: string, questionId: string, optionId: string) {
    return this.record('save', questionId, [optionId]);
  }

  saveAnswers(_attemptId: string, questionId: string, optionIds: string[]) {
    return this.record('saveSet', questionId, optionIds);
  }

  clearAnswer(_attemptId: string, questionId: string) {
    return this.record('clear', questionId, []);
  }

  private record(kind: Call['kind'], questionId: string, optionIds: string[]) {
    const reply = new Subject<void>();
    this.calls.push({ kind, questionId, optionIds, reply });
    return reply;
  }
}

const failure = (status: number) => new HttpErrorResponse({ status, statusText: 'x' });

describe('AnswerSync (FR-53 resumable autosave)', () => {
  let api: FakeApi;
  let refused: PendingAnswer[];

  const create = (persist = false, attemptId = 'a1') =>
    new AnswerSync({ api, attemptId, persist, onRefused: (answer) => refused.push(answer) });

  /** Lets what is waiting on a reply, or on a timer, run. */
  const settle = (ms = 0) => vi.advanceTimersByTimeAsync(ms);

  const succeed = async (call: Call) => {
    call.reply.next();
    call.reply.complete();
    await settle();
  };

  const fail = async (call: Call, status = 0) => {
    call.reply.error(failure(status));
    await settle();
  };

  const change = (questionId: string, optionIds: string[], previous: string[] = [], multiple = false) => ({ questionId, optionIds, multiple, previous });

  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
    api = new FakeApi();
    refused = [];
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('sends a choice at once, and has nothing waiting once the server has it', async () => {
    const sync = create();

    sync.enqueue(change('q1', ['o1']));

    expect(sync.pendingCount()).toBe(1);
    expect(api.calls).toMatchObject([{ kind: 'save', questionId: 'q1', optionIds: ['o1'] }]);

    await succeed(api.calls[0]);

    expect(sync.pendingCount()).toBe(0);
    expect(sync.waiting()).toBe(false);
    expect(sync.entries()).toEqual([]);
  });

  it('saves a set for a question that takes several, and clears an answer that was taken back', async () => {
    const sync = create();

    sync.enqueue(change('q1', ['o1', 'o2'], [], true));
    await succeed(api.calls[0]);
    sync.enqueue(change('q1', [], ['o1', 'o2'], true));

    expect(api.calls.map((c) => c.kind)).toEqual(['saveSet', 'clear']);
    expect(api.calls[0].optionIds).toEqual(['o1', 'o2']);
  });

  it('sends one answer at a time, in the order they were chosen', async () => {
    const sync = create();

    sync.enqueue(change('q1', ['o1']));
    sync.enqueue(change('q2', ['o2']));
    expect(api.calls).toHaveLength(1);

    await succeed(api.calls[0]);
    expect(api.calls.map((c) => c.questionId)).toEqual(['q1', 'q2']);
    await succeed(api.calls[1]);
    expect(sync.pendingCount()).toBe(0);
  });

  it('sends only the latest choice for a question, never one it replaced before it was sent', async () => {
    const sync = create();
    sync.enqueue(change('q1', ['o1']));
    sync.enqueue(change('q1', ['o2'], ['o1']));
    sync.enqueue(change('q1', ['o3'], ['o2']));

    await succeed(api.calls[0]);
    await succeed(api.calls[1]);

    expect(api.calls.map((c) => c.optionIds)).toEqual([['o1'], ['o3']]);
    expect(sync.pendingCount()).toBe(0);
  });

  describe('when the server cannot be reached', () => {
    it.each([0, 500, 502, 503, 408, 429, 401])('keeps the answer and tries again, for a %d', async (status) => {
      const sync = create();
      sync.enqueue(change('q1', ['o1']));

      await fail(api.calls[0], status);

      expect(sync.waiting()).toBe(true);
      expect(sync.pendingCount()).toBe(1);
      expect(refused).toEqual([]);

      await settle(RETRY_DELAYS_MS[0]);
      expect(api.calls).toHaveLength(2);
      await succeed(api.calls[1]);

      expect(sync.waiting()).toBe(false);
      expect(sync.pendingCount()).toBe(0);
    });

    it('waits longer after each failure in a row, up to a limit', async () => {
      const sync = create();
      sync.enqueue(change('q1', ['o1']));

      for (let i = 0; i < RETRY_DELAYS_MS.length + 2; i++) {
        const expected = RETRY_DELAYS_MS[Math.min(i, RETRY_DELAYS_MS.length - 1)];
        await fail(api.calls[i]);
        await settle(expected - 1);
        expect(api.calls).toHaveLength(i + 1);
        await settle(1);
        expect(api.calls).toHaveLength(i + 2);
      }

      expect(RETRY_DELAYS_MS[RETRY_DELAYS_MS.length - 1]).toBe(30_000);
    });

    it('tries at once when the browser says it is back online', async () => {
      const sync = create();
      sync.enqueue(change('q1', ['o1']));
      await fail(api.calls[0]);

      globalThis.dispatchEvent(new Event('online'));
      await settle();

      expect(api.calls).toHaveLength(2);
      sync.dispose();
    });

    it('tries at once for a newly chosen answer too, and sends both when it is back', async () => {
      const sync = create();
      sync.enqueue(change('q1', ['o1']));
      await fail(api.calls[0]);

      sync.enqueue(change('q2', ['o2']));
      await fail(api.calls[1]);
      await settle(RETRY_DELAYS_MS[1]);
      await succeed(api.calls[2]);
      await succeed(api.calls[3]);

      expect(api.calls.map((c) => c.questionId)).toEqual(['q1', 'q1', 'q1', 'q2']);
      expect(sync.pendingCount()).toBe(0);
    });
  });

  describe('when the server refuses', () => {
    it.each([400, 403, 404, 409])('puts the choice back to what the server last had, for a %d, and goes on', async (status) => {
      const sync = create();
      sync.enqueue(change('q1', ['o2'], ['o1']));
      sync.enqueue(change('q2', ['o9']));

      await fail(api.calls[0], status);

      expect(refused).toEqual([{ questionId: 'q1', optionIds: ['o2'], multiple: false, confirmed: ['o1'] }]);
      expect(sync.pendingCount()).toBe(1);
      expect(sync.waiting()).toBe(false);
      expect(api.calls.map((c) => c.questionId)).toEqual(['q1', 'q2']);
    });

    it('puts a question back to what the server last confirmed, however many choices it took to get here', async () => {
      const sync = create();
      sync.enqueue(change('q1', ['o2'], ['o1']));
      sync.enqueue(change('q1', ['o3'], ['o2']));

      await succeed(api.calls[0]);
      await fail(api.calls[1], 409);

      // The first choice reached the server, so what it last confirmed is o2, not the o1 from before.
      expect(refused.map((r) => r.confirmed)).toEqual([['o2']]);
    });

    it('says nothing of a refused choice that a newer one has already replaced', async () => {
      const sync = create();
      sync.enqueue(change('q1', ['o2'], ['o1']));
      sync.enqueue(change('q1', ['o3'], ['o2']));

      await fail(api.calls[0], 409);

      expect(refused).toEqual([]);
      expect(api.calls.map((c) => c.optionIds)).toEqual([['o2'], ['o3']]);
    });
  });

  describe('on this device', () => {
    it('keeps what is waiting, so a reload can send it', async () => {
      const sync = create(true);
      sync.enqueue(change('q1', ['o1'], ['o0']));
      await fail(api.calls[0]);
      sync.dispose();

      const reloaded = create(true);

      expect(reloaded.pendingCount()).toBe(1);
      expect(reloaded.entries()).toEqual([{ questionId: 'q1', optionIds: ['o1'], multiple: false, confirmed: ['o0'] }]);
      reloaded.flush();
      expect(api.calls).toHaveLength(2);
      await succeed(api.calls[1]);
      expect(localStorage.getItem('exam.pendingAnswers.a1')).toBeNull();
    });

    it('keeps each attempt’s answers apart', async () => {
      create(true, 'a1').enqueue(change('q1', ['o1']));

      expect(create(true, 'a2').pendingCount()).toBe(0);
    });

    it('is not written down for a preview', () => {
      create(false).enqueue(change('q1', ['o1']));

      expect(localStorage.getItem('exam.pendingAnswers.a1')).toBeNull();
    });

    it('ignores what was stored if it is not a list of answers', () => {
      localStorage.setItem('exam.pendingAnswers.a1', JSON.stringify([{ questionId: 1 }, 'x', { questionId: 'q1', optionIds: ['o1'], multiple: false, confirmed: [] }]));

      expect(create(true).entries().map((e) => e.questionId)).toEqual(['q1']);
      localStorage.setItem('exam.pendingAnswers.a1', 'not json');
      expect(create(true).pendingCount()).toBe(0);
    });

    it('forgets everything waiting when the attempt is over', async () => {
      const sync = create(true);
      sync.enqueue(change('q1', ['o1']));
      await fail(api.calls[0]);

      sync.discard();

      expect(sync.pendingCount()).toBe(0);
      expect(localStorage.getItem('exam.pendingAnswers.a1')).toBeNull();
    });

    it('works without a store that can be written to', () => {
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new Error('blocked');
      });

      expect(() => create(true).enqueue(change('q1', ['o1']))).not.toThrow();
      expect(api.calls).toHaveLength(1);
    });
  });

  describe('whenSettled, for a submit that must not leave an answer behind', () => {
    it('answers true at once when nothing is waiting', () => {
      const done = vi.fn();

      create().whenSettled(done);

      expect(done).toHaveBeenCalledExactlyOnceWith(true);
      expect(api.calls).toHaveLength(0);
    });

    it('waits for what is on its way, and answers true once the server has it', async () => {
      const sync = create();
      sync.enqueue(change('q1', ['o1']));
      const done = vi.fn();
      sync.whenSettled(done);

      expect(done).not.toHaveBeenCalled();
      await succeed(api.calls[0]);

      expect(done).toHaveBeenCalledExactlyOnceWith(true);
    });

    it('answers false when the server cannot be reached, and the answers are kept', async () => {
      const sync = create();
      sync.enqueue(change('q1', ['o1']));
      await fail(api.calls[0]);
      const done = vi.fn();

      sync.whenSettled(done);
      expect(api.calls).toHaveLength(2);
      await fail(api.calls[1]);

      expect(done).toHaveBeenCalledExactlyOnceWith(false);
      expect(sync.pendingCount()).toBe(1);
    });
  });

  it('stops trying when it is disposed, but leaves what is waiting for the next time', async () => {
    const sync = create(true);
    sync.enqueue(change('q1', ['o1']));
    await fail(api.calls[0]);

    sync.dispose();
    await settle(60_000);

    expect(api.calls).toHaveLength(1);
    expect(localStorage.getItem('exam.pendingAnswers.a1')).not.toBeNull();
  });
});

describe('isRefusal', () => {
  it.each([
    [400, true],
    [403, true],
    [404, true],
    [409, true],
    [401, false],
    [408, false],
    [429, false],
    [500, false],
    [503, false],
    [0, false],
  ])('treats a %d as a refusal: %s', (status, expected) => {
    expect(isRefusal(failure(status))).toBe(expected);
  });

  it('treats anything that is not an HTTP answer as not a refusal', () => {
    expect(isRefusal(new Error('boom'))).toBe(false);
    expect(isRefusal(undefined)).toBe(false);
  });
});
