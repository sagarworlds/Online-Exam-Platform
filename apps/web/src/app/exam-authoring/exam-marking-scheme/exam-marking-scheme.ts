import { Component, effect, input, output, signal, untracked } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { MarkingSchemeDto } from '../exam.models';

/** The largest mark one question can be worth or cost, and the finest step; mirrors what the API enforces. */
const LARGEST_MAGNITUDE = 100;
const MOST_DECIMAL_PLACES = 2;

/** One mark field's text, kept as typed so a half-typed "-0." is not rewritten under the author's cursor. */
interface MarkDraft {
  correct: string;
  incorrect: string;
  unattempted: string;
  partialCredit: boolean;
}

/**
 * The marks a correct, an incorrect and an unattempted question earn (FR-12), as a small card. Like the attempt-limit card it only
 * collects the numbers and the editor page sends them. Only a draft exam can change them, because new marks would make attempts
 * scored earlier disagree with attempts scored later; once published the card just shows them.
 */
@Component({
  selector: 'app-exam-marking-scheme',
  imports: [TranslatePipe],
  templateUrl: './exam-marking-scheme.html',
})
export class ExamMarkingScheme {
  /** The marks the exam has now. */
  readonly scheme = input.required<MarkingSchemeDto>();
  /** Whether the marks can be changed; true only for a draft. */
  readonly editable = input(true);
  /** True while a request about the exam is running, so the button cannot be pressed twice. */
  readonly busy = input(false);

  /** The author saved new marks; the page sends them. */
  readonly changed = output<MarkingSchemeDto>();

  protected readonly largest = LARGEST_MAGNITUDE;
  protected readonly editing = signal(false);
  protected readonly draft = signal<MarkDraft>({ correct: '', incorrect: '', unattempted: '', partialCredit: false });

  /** The scheme that was shown when the form was last reset; undefined before the first look. */
  private shown: MarkingSchemeDto | undefined;

  constructor() {
    // Once the exam carries the new marks the form has done its job; close it. A refusal leaves them as they were, so the form
    // stays open with what the author typed.
    effect(() => {
      const scheme = this.scheme();
      untracked(() => {
        if (!this.shown || !sameMarks(scheme, this.shown)) {
          this.shown = scheme;
          this.editing.set(false);
        }
      });
    });
  }

  /** The three fields as numbers in range with the right signs, or null while any is not. */
  protected parsedDraft(): MarkingSchemeDto | null {
    const { correct, incorrect, unattempted, partialCredit } = this.draft();
    const correctMarks = parseMark(correct);
    const incorrectMarks = parseMark(incorrect);
    const unattemptedMarks = parseMark(unattempted);
    if (correctMarks === null || incorrectMarks === null || unattemptedMarks === null) {
      return null;
    }

    const inRange =
      correctMarks > 0 && correctMarks <= LARGEST_MAGNITUDE && incorrectMarks <= 0 && incorrectMarks >= -LARGEST_MAGNITUDE && unattemptedMarks <= 0 && unattemptedMarks >= -LARGEST_MAGNITUDE;
    return inRange ? { correctMarks, incorrectMarks, unattemptedMarks, partialCredit } : null;
  }

  protected setPartialCredit(on: boolean): void {
    this.draft.update((current) => ({ ...current, partialCredit: on }));
  }

  protected setField(field: 'correct' | 'incorrect' | 'unattempted', value: string): void {
    this.draft.update((current) => ({ ...current, [field]: value }));
  }

  protected startEditing(): void {
    const { correctMarks, incorrectMarks, unattemptedMarks, partialCredit } = this.scheme();
    this.draft.set({
      correct: String(correctMarks),
      incorrect: String(incorrectMarks),
      unattempted: String(unattemptedMarks),
      partialCredit: partialCredit === true,
    });
    this.editing.set(true);
  }

  protected save(event: Event): void {
    event.preventDefault();
    const value = this.parsedDraft();
    if (value === null || this.busy()) {
      return;
    }

    // Nothing to send for the marks it already has; just close.
    if (sameMarks(value, this.scheme())) {
      this.editing.set(false);
      return;
    }

    this.changed.emit(value);
  }
}

/** A mark as a finite number with at most two decimal places, or null for anything else (blank, text, 1.005). */
function parseMark(text: string): number | null {
  const trimmed = text.trim();
  const value = Number(trimmed);
  if (trimmed === '' || !Number.isFinite(value)) {
    return null;
  }

  const scaled = Math.round(value * 10 ** MOST_DECIMAL_PLACES);
  // Compare in hundredths so 0.1 + 0.2 style float noise does not read as a third decimal place.
  return Math.abs(value * 10 ** MOST_DECIMAL_PLACES - scaled) < 1e-9 ? value : null;
}

function sameMarks(a: MarkingSchemeDto, b: MarkingSchemeDto): boolean {
  return (
    a.correctMarks === b.correctMarks &&
    a.incorrectMarks === b.incorrectMarks &&
    a.unattemptedMarks === b.unattemptedMarks &&
    (a.partialCredit ?? false) === (b.partialCredit ?? false)
  );
}
