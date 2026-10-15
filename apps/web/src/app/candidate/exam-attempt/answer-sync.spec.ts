import { HttpErrorResponse } from '@angular/common/http';
import { Subject } from 'rxjs';
import { afterEach, beforeEach, vi } from 'vitest';
import { AnswerSync, AnswerSyncApi, PendingAnswer, RETRY_DELAYS_MS, heldSequence, isRefusal } from './answer-sync';

interface Call {
  kind: 'save' | 'saveSet' | 'saveText' | 'clear';
  questionId: string;
  optionIds: string[];
  /** The typed answer, for a `saveText` call. */
  text?: string;
  /** The sequence number the write carried. */
  sequence: number;
  reply: Subject<void>;
}

/** A fake API whose calls wait until the test answers them, so the order and the failures are the test's to choose. */
class FakeApi implements AnswerSyncApi {
  readonly calls: Call[] = [];

  saveAnswer(_attemptId: string, questionId: string, optionId: string, sequence: number) {
    return this.record('save', questionId, [optionId], sequence);
  }

  saveAnswers(_attemptId: string, questionId: string, optionIds: string[], sequence: number) {
    return this.record('saveSet', questionId, optionIds, sequence);
  }

  saveText(_attemptId: string, questionId: string, text: string, sequence: number) {
    return this.record('saveText', questionId, [], sequence, text);
  }

  clearAnswer(_attemptId: string, questionId: string, sequence: number) {
    return this.record('clear', questionId, [], sequence);
  }

  private record(kind: Call['kind'], questionId: string, optionIds: string[], sequence: number, text?: string) {
    const reply = new Subject<void>();
    this.calls.push({ kind, questionId, optionIds, text, sequence, reply });
    return reply;
  }
}

const failure = (status: number) => new HttpErrorResponse({ status, statusText: 'x' });

/** A problem response, as the API writes one: the error code in `title`, and any extra facts beside it. */
const problem = (status: number, title: string, extra: Record<string, unknown> = {}) =>
  new HttpErrorResponse({ status, statusText: 'x', error: { title, ...extra } });

/** The server's answer to a write it did not apply because it already holds a number at least as high. */
const superseded = (held: number) => problem(409, 'answer_superseded', { currentSequence: held });

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

  const failWith = async (call: Call, error: HttpErrorResponse) => {
    call.reply.error(error);
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

  describe('a typed answer to a text question', () => {
    const typed = (questionId: string, text: string, previousText: string | null = null) => ({
      questionId,
      optionIds: [],
      multiple: false,
      previous: [],
      text,
      previousText,
    });

    it('is sent as the text typed, and nothing is waiting once the server has it', async () => {
      const sync = create();

      sync.enqueue(typed('q1', 'Paris'));

      expect(api.calls).toMatchObject([{ kind: 'saveText', questionId: 'q1', text: 'Paris' }]);
      await succeed(api.calls[0]);
      expect(sync.pendingCount()).toBe(0);
    });

    it('takes the answer back when the text is blank, as clearing it does', () => {
      const sync = create();

      sync.enqueue(typed('q1', '   ', 'Paris'));

      expect(api.calls).toMatchObject([{ kind: 'clear', questionId: 'q1' }]);
    });

    it('puts the text back to what the server last had when it refuses it', async () => {
      const sync = create();
      sync.enqueue(typed('q1', 'Lyon', 'Paris'));

      await fail(api.calls[0], 409);

      expect(refused).toMatchObject([{ questionId: 'q1', text: 'Lyon', confirmedText: 'Paris' }]);
      expect(sync.pendingCount()).toBe(0);
    });

    it('keeps the typed text and sends it again when the connection is down', async () => {
      const sync = create();
      sync.enqueue(typed('q1', 'Paris'));

      await fail(api.calls[0], 0);
      await settle(RETRY_DELAYS_MS[0]);

      expect(api.calls.map((c) => c.text)).toEqual(['Paris', 'Paris']);
      expect(sync.pendingCount()).toBe(1);
    });

    it('remembers a typed answer on this device until the server has it', () => {
      const sync = create(true);
      sync.enqueue(typed('q1', 'Paris'));

      expect(create(true).entries()).toMatchObject([{ questionId: 'q1', text: 'Paris' }]);
      expect(sync.entries()).toHaveLength(1);
    });

    it('reads an entry saved before text answers existed as a choice', () => {
      localStorage.setItem('exam.pendingAnswers.a1', JSON.stringify([{ questionId: 'q1', optionIds: ['o1'], multiple: false, confirmed: [] }]));

      expect(create(true).entries()).toEqual([{ questionId: 'q1', optionIds: ['o1'], multiple: false, confirmed: [], text: null, confirmedText: null, sequence: 1 }]);
    });
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

  describe('sequence numbers (NFR-4, ADR 0004)', () => {
    it('numbers each write to a question one higher than the last, and each question from one', async () => {
      const sync = create();

      sync.enqueue(change('q1', ['o1']));
      await succeed(api.calls[0]);
      sync.enqueue(change('q1', ['o2'], ['o1']));
      await succeed(api.calls[1]);
      sync.enqueue(change('q2', ['o9']));

      expect(api.calls.map((c) => [c.questionId, c.sequence])).toEqual([
        ['q1', 1],
        ['q1', 2],
        ['q2', 1],
      ]);
    });

    it('gives a choice that replaces one not yet sent the higher number, and never sends the lower', async () => {
      const sync = create();
      sync.enqueue(change('q1', ['o1']));
      sync.enqueue(change('q1', ['o2'], ['o1']));
      sync.enqueue(change('q1', ['o3'], ['o2']));

      await succeed(api.calls[0]);
      await succeed(api.calls[1]);

      expect(api.calls.map((c) => [c.optionIds[0], c.sequence])).toEqual([
        ['o1', 1],
        ['o3', 3],
      ]);
    });

    it('sends a write again with the same number, because the repeat of a write is not a newer write', async () => {
      const sync = create();
      sync.enqueue(change('q1', ['o1']));

      await fail(api.calls[0], 503);
      await settle(RETRY_DELAYS_MS[0]);

      expect(api.calls.map((c) => c.sequence)).toEqual([1, 1]);
    });

    it('numbers the next write above the number the server holds, so a reload does not start again from one', () => {
      const sync = create();
      sync.observe('q1', 7);

      sync.enqueue(change('q1', ['o1']));

      expect(api.calls[0].sequence).toBe(8);
    });

    it('never lowers a number it knows because a read of the attempt is older', () => {
      const sync = create();
      sync.observe('q1', 7);
      sync.observe('q1', 3);
      sync.observe('q1', null);
      sync.observe('q1', undefined);

      sync.enqueue(change('q1', ['o1']));

      expect(api.calls[0].sequence).toBe(8);
    });

    it('keeps the number of a write waiting on the device, and numbers the next one above it', async () => {
      const sync = create(true);
      sync.enqueue(change('q1', ['o1']));
      await fail(api.calls[0]);
      sync.dispose();

      const reloaded = create(true);
      reloaded.enqueue(change('q1', ['o2'], ['o1']));

      expect(JSON.parse(localStorage.getItem('exam.pendingAnswers.a1') ?? '[]')).toMatchObject([{ questionId: 'q1', sequence: 2 }]);
      expect(api.calls.map((c) => c.sequence)).toEqual([1, 2]);
    });

    describe('when the server says it already holds a newer write (409 answer_superseded)', () => {
      it('sends the write the candidate still wants again, above the number the server holds, and does not put the choice back', async () => {
        const sync = create();
        sync.enqueue(change('q1', ['o2'], ['o1']));

        await failWith(api.calls[0], superseded(5));

        expect(refused).toEqual([]);
        expect(api.calls.map((c) => [c.optionIds[0], c.sequence])).toEqual([
          ['o2', 1],
          ['o2', 6],
        ]);
        await succeed(api.calls[1]);
        expect(sync.pendingCount()).toBe(0);
        expect(sync.waiting()).toBe(false);
      });

      it('does the same for a clear', async () => {
        const sync = create();
        sync.enqueue(change('q1', [], ['o1']));

        await failWith(api.calls[0], superseded(4));

        expect(api.calls.map((c) => [c.kind, c.sequence])).toEqual([
          ['clear', 1],
          ['clear', 5],
        ]);
      });

      it('drops a write that a newer choice has replaced, since the newer one carries its own number', async () => {
        const sync = create();
        sync.enqueue(change('q1', ['o2'], ['o1']));
        sync.enqueue(change('q1', ['o3'], ['o2']));

        await failWith(api.calls[0], superseded(1));

        expect(refused).toEqual([]);
        expect(api.calls.map((c) => [c.optionIds[0], c.sequence])).toEqual([
          ['o2', 1],
          ['o3', 2],
        ]);
      });

      it('remembers the number so the next choice goes above it too', async () => {
        const sync = create();
        sync.enqueue(change('q1', ['o2'], ['o1']));
        await failWith(api.calls[0], superseded(9));
        await succeed(api.calls[1]);

        sync.enqueue(change('q1', ['o3'], ['o2']));

        expect(api.calls[2].sequence).toBe(11);
      });

      it('keeps the number it sent again on the device', async () => {
        const sync = create(true);
        sync.enqueue(change('q1', ['o2'], ['o1']));

        await failWith(api.calls[0], superseded(5));

        expect(JSON.parse(localStorage.getItem('exam.pendingAnswers.a1') ?? '[]')).toMatchObject([{ questionId: 'q1', sequence: 6 }]);
      });

      it('waits and tries again, as for any failure, when another writer keeps getting there first', async () => {
        const sync = create();
        sync.enqueue(change('q1', ['o2'], ['o1']));

        // Five times in a row the write goes again above a number that was already taken.
        for (let held = 10; held < 15; held++) {
          await failWith(api.calls[api.calls.length - 1], superseded(held));
        }
        expect(api.calls).toHaveLength(6);

        // The sixth refusal does not loop: the answer is kept, and sent again after a delay.
        await failWith(api.calls[5], superseded(20));
        expect(api.calls).toHaveLength(6);
        expect(sync.waiting()).toBe(true);
        expect(sync.pendingCount()).toBe(1);
        expect(refused).toEqual([]);

        // The write that waits already carries a number above the one the server held, so the next try is not refused for it again.
        await settle(RETRY_DELAYS_MS[0]);
        expect(api.calls).toHaveLength(7);
        expect(api.calls[6].sequence).toBe(21);
      });
    });

    describe('when the write lost a race with another change to the same answer (409 concurrency_conflict)', () => {
      it('keeps the choice and sends it again after a delay, instead of putting it back', async () => {
        const sync = create();
        sync.enqueue(change('q1', ['o2'], ['o1']));

        await failWith(api.calls[0], problem(409, 'concurrency_conflict'));

        expect(refused).toEqual([]);
        expect(sync.waiting()).toBe(true);
        expect(sync.pendingCount()).toBe(1);
        await settle(RETRY_DELAYS_MS[0]);
        expect(api.calls.map((c) => c.sequence)).toEqual([1, 1]);
      });
    });

    describe('heldSequence and isRefusal', () => {
      it('reads the number from an answer_superseded problem, and from nothing else', () => {
        expect(heldSequence(superseded(12))).toBe(12);
        expect(heldSequence(problem(409, 'attempt_not_in_progress'))).toBeNull();
        expect(heldSequence(problem(409, 'answer_superseded'))).toBeNull();
        expect(heldSequence(problem(409, 'answer_superseded', { currentSequence: '12' }))).toBeNull();
        expect(heldSequence(problem(400, 'answer_superseded', { currentSequence: 12 }))).toBeNull();
        expect(heldSequence(new HttpErrorResponse({ status: 409, statusText: 'x', error: 'text' }))).toBeNull();
        expect(heldSequence(new HttpErrorResponse({ status: 409, statusText: 'x' }))).toBeNull();
        expect(heldSequence(new Error('x'))).toBeNull();
      });

      it('does not count a lost race as a refusal, but still counts an attempt that is over', () => {
        expect(isRefusal(problem(409, 'concurrency_conflict'))).toBe(false);
        expect(isRefusal(problem(409, 'attempt_not_in_progress'))).toBe(true);
        expect(isRefusal(failure(409))).toBe(true);
      });
    });
  });

  describe('when the server refuses', () => {
    it.each([400, 403, 404, 409])('puts the choice back to what the server last had, for a %d, and goes on', async (status) => {
      const sync = create();
      sync.enqueue(change('q1', ['o2'], ['o1']));
      sync.enqueue(change('q2', ['o9']));

      await fail(api.calls[0], status);

      expect(refused).toEqual([{ questionId: 'q1', optionIds: ['o2'], multiple: false, confirmed: ['o1'], text: null, confirmedText: null, sequence: 1 }]);
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
      expect(reloaded.entries()).toEqual([{ questionId: 'q1', optionIds: ['o1'], multiple: false, confirmed: ['o0'], text: null, confirmedText: null, sequence: 1 }]);
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
