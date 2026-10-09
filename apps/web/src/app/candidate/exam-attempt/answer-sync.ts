import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { Observable } from 'rxjs';

/** What the answer queue needs of the API: the ways an answer is saved. */
export interface AnswerSyncApi {
  saveAnswer(attemptId: string, questionId: string, optionId: string): Observable<void>;
  saveAnswers(attemptId: string, questionId: string, optionIds: string[]): Observable<void>;
  saveText(attemptId: string, questionId: string, text: string): Observable<void>;
  clearAnswer(attemptId: string, questionId: string): Observable<void>;
}

/**
 * What a candidate has answered for one question and the server has not confirmed yet: a set of options, or for a text question the text
 * typed (`text` is then not null, and `optionIds` is empty).
 */
export interface PendingAnswer {
  questionId: string;
  /** The whole set chosen; empty takes the answer back. */
  optionIds: string[];
  /** Whether the question takes a set of options (a checkbox question) rather than one. */
  multiple: boolean;
  /** What the server last confirmed, so a refusal can put the page back to it. */
  confirmed: string[];
  /** The text typed for a text question, or null for one answered by choosing options. Blank text takes the answer back. */
  text: string | null;
  /** The text the server last confirmed for a text question, so a refusal can put the page back to it. */
  confirmedText: string | null;
}

/** How the page tells the queue about a choice: a set of options, or the text typed for a text question. */
export interface AnswerChange {
  questionId: string;
  optionIds: string[];
  multiple: boolean;
  /** What the question showed just before this choice. */
  previous: string[];
  /** The text typed for a text question; omitted or null for a choice of options. */
  text?: string | null;
  /** The text the question showed just before this typed answer. */
  previousText?: string | null;
}

/**
 * Whether a pending answer takes the answer back rather than saving one: nothing chosen, or for a text question nothing typed. The
 * queue clears the answer on the server then, and the page says it could not clear it when that is refused.
 */
export function isEmptyAnswer(answer: Pick<PendingAnswer, 'optionIds' | 'text'>): boolean {
  return answer.text === null ? answer.optionIds.length === 0 : answer.text.trim().length === 0;
}

export interface AnswerSyncOptions {
  api: AnswerSyncApi;
  attemptId: string;
  /** Called with a choice the server refused, so the page can put the question back as the server has it. */
  onRefused: (answer: PendingAnswer, error: unknown) => void;
  /** Whether to keep what is waiting on this device, so a reload does not lose it. Not for a preview, which saves nothing anyway. */
  persist: boolean;
}

/** How long to wait before the next try after each failure in a row; the last is where it stays. */
export const RETRY_DELAYS_MS = [2_000, 4_000, 8_000, 16_000, 30_000] as const;

const storageKey = (attemptId: string) => `exam.pendingAnswers.${attemptId}`;

/**
 * Whether the server turned the request down for a reason that waiting will not change (an attempt that is over, a locked section, a
 * question that is not in the attempt). A connection that is down, a server that is busy or waking, or a request that was too soon or
 * too many are not refusals: the answer is kept and sent again.
 */
export function isRefusal(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500 && ![401, 408, 429].includes(error.status);
}

/**
 * Saves a candidate's answers without losing them to a bad connection (FR-53, resumable autosave). A choice shows at once and joins a
 * queue that holds only the latest choice for each question; the queue sends them one at a time, and when the connection is down or the
 * server is not answering it keeps them, tries again with a growing delay and as soon as the browser says it is online again, and
 * remembers them on this device, so closing or reloading the page does not lose them either. Saving an answer is idempotent on the
 * server, so a repeat is harmless. Only a refusal (see `isRefusal`) puts a choice back.
 */
export class AnswerSync {
  /** How many choices are waiting to reach the server. */
  readonly pendingCount = signal(0);

  /** Whether the last try could not reach the server, so what is waiting is waiting for a connection. */
  readonly waiting = signal(false);

  private readonly pending = new Map<string, PendingAnswer>();
  private readonly settledWaiters: ((ok: boolean) => void)[] = [];
  private readonly onOnline = () => this.flush();
  private running = false;
  private disposed = false;
  private failures = 0;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(private readonly options: AnswerSyncOptions) {
    this.restore();
    globalThis.addEventListener?.('online', this.onOnline);
  }

  /** What is waiting, oldest first; the page shows it over what it read from the server. */
  entries(): PendingAnswer[] {
    return [...this.pending.values()];
  }

  /** Queues a choice, replacing any earlier one for the question that has not been sent, and starts sending. */
  enqueue(change: AnswerChange): void {
    const waiting = this.pending.get(change.questionId);
    this.pending.set(change.questionId, {
      questionId: change.questionId,
      optionIds: change.optionIds,
      multiple: change.multiple,
      text: change.text ?? null,
      // The server's last word stays what it was across choices that never reached it, for options and for typed text alike.
      confirmed: waiting?.confirmed ?? change.previous,
      confirmedText: waiting ? waiting.confirmedText : (change.previousText ?? null),
    });
    this.changed();
    this.flush();
  }

  /** Sends what is waiting now, without waiting for the next retry. */
  flush(): void {
    if (this.running || this.disposed) {
      return;
    }

    this.clearRetry();
    this.running = true;
    this.sendNext();
  }

  /**
   * Tries to get everything waiting to the server, for a submit that must not leave an answer behind, and says how it went.
   * @param done Told true once nothing is waiting, at once if nothing was; told false if the server could not be reached, with the
   *   answers kept for later.
   */
  whenSettled(done: (saved: boolean) => void): void {
    if (this.pending.size === 0 && !this.running) {
      done(true);
      return;
    }

    this.settledWaiters.push(done);
    this.flush();
  }

  /** Forgets everything waiting, on this device too: the attempt is over and nothing more can be saved to it. */
  discard(): void {
    this.pending.clear();
    this.changed();
  }

  /** Stops trying; what is waiting stays on this device for the next time the attempt is opened. */
  dispose(): void {
    this.disposed = true;
    this.clearRetry();
    globalThis.removeEventListener?.('online', this.onOnline);
    this.settle(false);
  }

  private sendNext(): void {
    if (this.disposed) {
      this.running = false;
      return;
    }

    const next = this.pending.values().next().value;
    if (next === undefined) {
      this.running = false;
      this.failures = 0;
      this.waiting.set(false);
      this.settle(true);
      return;
    }

    // Callbacks and not promises, so what an answer from the server changes on the page happens when the answer arrives.
    this.send(next).subscribe({
      error: (error: unknown) => this.failed(next, error),
      complete: () => this.succeeded(next),
    });
  }

  private succeeded(answer: PendingAnswer): void {
    this.failures = 0;
    this.waiting.set(false);
    const current = this.pending.get(answer.questionId);
    if (current === answer) {
      this.pending.delete(answer.questionId);
      this.changed();
    } else if (current !== undefined) {
      // A newer choice replaced this one while it was on its way. The server has this one now, so that is what a refusal of the
      // newer choice must put the question back to.
      current.confirmed = answer.optionIds;
      current.confirmedText = answer.text;
      this.changed();
    }

    this.sendNext();
  }

  private failed(answer: PendingAnswer, error: unknown): void {
    if (isRefusal(error)) {
      // Only a choice that is still the latest is put back; a newer one for the same question will speak for itself.
      if (this.pending.get(answer.questionId) === answer) {
        this.pending.delete(answer.questionId);
        this.changed();
        this.options.onRefused(answer, error);
      }

      this.sendNext();
      return;
    }

    this.running = false;
    this.waiting.set(true);
    this.failures++;
    this.scheduleRetry();
    this.settle(false);
  }

  private send(answer: PendingAnswer): Observable<void> {
    const { api, attemptId } = this.options;
    if (isEmptyAnswer(answer)) {
      return api.clearAnswer(attemptId, answer.questionId);
    }
    if (answer.text !== null) {
      return api.saveText(attemptId, answer.questionId, answer.text);
    }

    return answer.multiple ? api.saveAnswers(attemptId, answer.questionId, answer.optionIds) : api.saveAnswer(attemptId, answer.questionId, answer.optionIds[0]);
  }

  private scheduleRetry(): void {
    const delay = RETRY_DELAYS_MS[Math.min(this.failures, RETRY_DELAYS_MS.length) - 1];
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      this.flush();
    }, delay);
  }

  private clearRetry(): void {
    if (this.retryTimer !== null) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }
  }

  private settle(ok: boolean): void {
    this.settledWaiters.splice(0).forEach((resolve) => resolve(ok));
  }

  private changed(): void {
    this.pendingCount.set(this.pending.size);
    if (!this.options.persist) {
      return;
    }

    try {
      if (this.pending.size === 0) {
        localStorage.removeItem(storageKey(this.options.attemptId));
      } else {
        localStorage.setItem(storageKey(this.options.attemptId), JSON.stringify(this.entries()));
      }
    } catch {
      // A blocked store only means a reload cannot bring them back; they are still sent from this page.
    }
  }

  private restore(): void {
    if (!this.options.persist) {
      return;
    }

    try {
      const stored: unknown = JSON.parse(localStorage.getItem(storageKey(this.options.attemptId)) ?? '[]');
      for (const entry of Array.isArray(stored) ? stored : []) {
        if (isPendingAnswer(entry)) {
          // Entries saved before text answers existed have no text, which is the same as none.
          this.pending.set(entry.questionId, { ...entry, text: entry.text ?? null, confirmedText: entry.confirmedText ?? null });
        }
      }
    } catch {
      // Unreadable is as good as nothing waiting.
    }

    this.pendingCount.set(this.pending.size);
  }
}

function isIdList(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((id) => typeof id === 'string');
}

function isPendingAnswer(value: unknown): value is PendingAnswer {
  const entry = value as Partial<PendingAnswer> | null;
  return (
    typeof entry === 'object' &&
    entry !== null &&
    typeof entry.questionId === 'string' &&
    typeof entry.multiple === 'boolean' &&
    isIdList(entry.optionIds) &&
    isIdList(entry.confirmed) &&
    isOptionalText(entry.text) &&
    isOptionalText(entry.confirmedText)
  );
}

/** Text as a queue saved before text answers existed may lack; a missing text is no text. */
function isOptionalText(value: unknown): value is string | null | undefined {
  return value === undefined || value === null || typeof value === 'string';
}
