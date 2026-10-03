import { Component, effect, input, output, signal, untracked } from '@angular/core';
import { ResultReleaseMode } from '../exam.models';

/** The fewest and most attempts an exam can give each candidate; mirrors the range the API enforces. */
const FEWEST_ATTEMPTS = 1;
const MOST_ATTEMPTS = 10;

/**
 * How many attempts every candidate has at an exam (FR-12), as a small card. It shows the number and, when the author asks to
 * change it, a field for the new one; it only collects the number, and the editor page sends it, so the card never needs to know
 * how the exam is stored or reloaded. It warns when more than one attempt would be combined with answers shown right after
 * each attempt, because a candidate then sees the right answers before their next try.
 */
@Component({
  selector: 'app-exam-attempt-limit',
  templateUrl: './exam-attempt-limit.html',
})
export class ExamAttemptLimit {
  /** The attempts every candidate has now. */
  readonly attempts = input.required<number>();
  /** Whether the number can be changed; false for an archived exam. */
  readonly editable = input(true);
  /** True while a request about the exam is running, so the button cannot be pressed twice. */
  readonly busy = input(false);
  /** When candidates see which answers were right, which decides whether the warning applies. */
  readonly releaseMode = input<ResultReleaseMode>('Instant');

  /** The author saved a new number; the page sends it. */
  readonly changed = output<number>();

  protected readonly fewest = FEWEST_ATTEMPTS;
  protected readonly most = MOST_ATTEMPTS;
  protected readonly editing = signal(false);
  protected readonly draft = signal('');

  /** The number that was shown when the form was last reset; undefined before the first look. */
  private shownAttempts: number | undefined;

  constructor() {
    // Once the exam carries the new number the form has done its job; close it. A refusal leaves the number as it was, so the
    // form stays open with what the author typed.
    effect(() => {
      const attempts = this.attempts();
      untracked(() => {
        if (attempts !== this.shownAttempts) {
          this.shownAttempts = attempts;
          this.editing.set(false);
        }
      });
    });
  }

  /** The draft as a whole number in range, or null while it is not one. */
  protected parsedDraft(): number | null {
    const text = this.draft().trim();
    const value = Number(text);
    return text !== '' && Number.isInteger(value) && value >= FEWEST_ATTEMPTS && value <= MOST_ATTEMPTS ? value : null;
  }

  /** Whether the number in the field, or the one shown when not editing, is more than one while answers are shown at once. */
  protected warns(attempts: number | null): boolean {
    return attempts !== null && attempts > 1 && this.releaseMode() === 'Instant';
  }

  protected startEditing(): void {
    this.draft.set(String(this.attempts()));
    this.editing.set(true);
  }

  protected save(event: Event): void {
    event.preventDefault();
    const value = this.parsedDraft();
    if (value === null || this.busy()) {
      return;
    }

    // Nothing to send for the number it already has; just close.
    if (value === this.attempts()) {
      this.editing.set(false);
      return;
    }

    this.changed.emit(value);
  }
}
