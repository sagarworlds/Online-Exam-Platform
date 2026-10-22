import { Injectable, signal } from '@angular/core';

/** What to ask, and how to word the answers. */
export interface ConfirmOptions {
  /** The question, as the dialog's title: "Revoke this invitation?". It is read out when the dialog opens. */
  title: string;
  /** What it means: who or what is affected, and that it cannot be undone when it cannot. Optional. */
  message?: string;
  /** What the confirmation will change, one line each: "Scope and marking can no longer be changed". Shown as a list under the message. */
  consequences?: readonly string[];
  /** The words on the button that goes ahead. A verb, not "OK": "Revoke invitation". */
  confirmLabel: string;
  /** The words on the button that does not. Defaults to "Cancel" in the page's language. */
  cancelLabel?: string;
  /** Whether going ahead cannot be undone: the confirm button is drawn as a danger button and Cancel has the focus, so a stray Enter is safe. */
  danger?: boolean;
}

/** A question waiting for its answer. */
export interface PendingConfirm {
  options: ConfirmOptions;
  resolve: (confirmed: boolean) => void;
}

/**
 * Asks a person "are you sure?" and says what they answered: `if (!(await confirm.ask({ ... }))) return;`. It is the one dialog for what
 * had been a browser `confirm()` (in English whatever the page's language, unstyled, and gone from a phone's screen reader), a bespoke
 * inline question per page, or nothing at all before something that cannot be undone. `ConfirmHost` draws it.
 *
 * A second question asked while one is open waits its turn. Closing the dialog any way but the confirm button, Escape included, is "no".
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly queue: PendingConfirm[] = [];
  private readonly showing = signal<PendingConfirm | null>(null);

  /** The question on screen, or null. */
  readonly current = this.showing.asReadonly();

  /** Resolves true when the person goes ahead, false when they do not. */
  ask(options: ConfirmOptions): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      this.queue.push({ options, resolve });
      if (this.showing() === null) {
        this.next();
      }
    });
  }

  /** The person answered (or closed the dialog): the question is put away and the next, if there is one, is shown. */
  answer(confirmed: boolean): void {
    const pending = this.showing();
    if (pending === null) {
      return;
    }
    this.showing.set(null);
    pending.resolve(confirmed);
    this.next();
  }

  private next(): void {
    const following = this.queue.shift();
    if (following !== undefined) {
      this.showing.set(following);
    }
  }
}
