import { Component, computed, input, output, signal } from '@angular/core';
import { FocusViolationLimitRequest } from '../exam.models';

/** The fewest and most times a candidate may leave the exam page when the exam watches for it; mirrors the range the API enforces (0 turns it off). */
const FEWEST_VIOLATIONS = 1;
const MOST_VIOLATIONS = 20;

/** The limit offered when an author first turns the watch on: strict enough to mean something, forgiving enough for one accident and a second. */
const SUGGESTED_LIMIT = 3;

/**
 * Whether the exam watches for a candidate leaving the exam page (switching tab or window, leaving full screen) and how many times
 * they may before the server ends the attempt (FR-22), as a small card. Like the other setting cards it only collects the choice and
 * the editor page sends it. It may change after publishing, because it changes nothing that is asked or scored.
 */
@Component({
  selector: 'app-exam-focus-violation-limit',
  templateUrl: './exam-focus-violation-limit.html',
})
export class ExamFocusViolationLimit {
  /** The limit now: 0 when the exam does not watch. */
  readonly limit = input.required<number>();
  /** Whether the choice can be changed; false only for an archived exam. */
  readonly editable = input(true);
  /** True while a request about the exam is running, so the button cannot be pressed twice. */
  readonly busy = input(false);

  /** The author saved a new limit; the page sends it. */
  readonly changed = output<FocusViolationLimitRequest>();

  protected readonly fewest = FEWEST_VIOLATIONS;
  protected readonly most = MOST_VIOLATIONS;

  /** What the author has chosen but not saved yet; null means "as the exam has it now". */
  private readonly pendingWatching = signal<boolean | null>(null);
  private readonly pendingNumber = signal<string | null>(null);

  protected readonly watching = computed(() => this.pendingWatching() ?? this.limit() > 0);
  protected readonly numberText = computed(() => this.pendingNumber() ?? String(this.limit() > 0 ? this.limit() : SUGGESTED_LIMIT));

  /** The limit that would be saved: 0 when not watching, else the number typed when it is a whole number in range, otherwise null. */
  protected readonly parsed = computed<number | null>(() => {
    if (!this.watching()) {
      return 0;
    }
    const text = this.numberText().trim();
    const value = Number(text);
    return text !== '' && Number.isInteger(value) && value >= FEWEST_VIOLATIONS && value <= MOST_VIOLATIONS ? value : null;
  });

  protected readonly dirty = computed(() => this.parsed() !== this.limit());

  protected toggle(watching: boolean): void {
    this.pendingWatching.set(watching);
  }

  protected typeNumber(text: string): void {
    this.pendingNumber.set(text);
  }

  protected save(event: Event): void {
    event.preventDefault();
    const value = this.parsed();
    if (value !== null && this.dirty() && this.editable() && !this.busy()) {
      this.changed.emit({ focusViolationLimit: value });
    }
  }

  protected discard(): void {
    this.pendingWatching.set(null);
    this.pendingNumber.set(null);
  }
}
