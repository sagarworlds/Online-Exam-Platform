import { Component, computed, inject, input, output, signal } from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';

/** The lowest and highest pass mark an author can choose; mirrors the range the API enforces. */
const LOWEST_PERCENT = 1;
const HIGHEST_PERCENT = 100;

/** The mark offered when an author first sets one, easy to change before saving. */
const SUGGESTED_PERCENT = 40;

/**
 * Whether the exam has a pass mark (FR-34) and what share of the total marks it is, as a small card. With no pass mark, results do not say
 * passed or failed. Like the other setting cards it only collects the choice: it emits the mark as a number, or null to remove it, and the
 * editor page sends it. It may change after publishing, because it changes nothing a candidate is asked or scored.
 */
@Component({
  selector: 'app-exam-passing-percentage',
  imports: [TranslatePipe],
  templateUrl: './exam-passing-percentage.html',
})
export class ExamPassingPercentage {
  private readonly i18n = inject(I18nService);

  /** The mark now, as a percentage, or null when the exam has none. */
  readonly percent = input.required<number | null>();
  /** Whether the choice can be changed; false only for an archived exam. */
  readonly editable = input(true);
  /** True while a request about the exam is running, so the button cannot be pressed twice. */
  readonly busy = input(false);

  /** The author saved a mark, or null to remove it; the page sends it. */
  readonly changed = output<number | null>();

  protected readonly lowest = LOWEST_PERCENT;
  protected readonly highest = HIGHEST_PERCENT;

  /** What the author has chosen but not saved yet; null means "as the exam has it now". */
  private readonly pendingSet = signal<boolean | null>(null);
  private readonly pendingNumber = signal<string | null>(null);

  protected readonly hasMark = computed(() => this.pendingSet() ?? this.percent() !== null);
  protected readonly numberText = computed(() => this.pendingNumber() ?? String(this.percent() ?? SUGGESTED_PERCENT));

  /**
   * The mark that would be saved: null when no mark is wanted (which removes it), the typed whole number when it is in range, and undefined
   * when the typed text is not a usable mark, which keeps Save disabled.
   */
  protected readonly parsed = computed<number | null | undefined>(() => {
    if (!this.hasMark()) {
      return null;
    }
    const text = this.numberText().trim();
    const value = Number(text);
    return text !== '' && Number.isInteger(value) && value >= LOWEST_PERCENT && value <= HIGHEST_PERCENT ? value : undefined;
  });

  /** Whether there is a change to offer. An unusable value still counts, so Save shows disabled and the author sees why it cannot be saved. */
  protected readonly dirty = computed(() => this.parsed() !== this.percent());

  /** What the page is set to, in words: a percentage, or none. */
  protected readState(): string {
    const percent = this.percent();
    return percent === null ? this.i18n.t('exams.passingMark.readNone') : this.i18n.t('exams.passingMark.readSet', { percent });
  }

  protected toggle(hasMark: boolean): void {
    this.pendingSet.set(hasMark);
  }

  protected typeNumber(text: string): void {
    this.pendingNumber.set(text);
  }

  protected save(event: Event): void {
    event.preventDefault();
    const value = this.parsed();
    if (value !== undefined && this.dirty() && this.editable() && !this.busy()) {
      this.changed.emit(value);
    }
  }

  protected discard(): void {
    this.pendingSet.set(null);
    this.pendingNumber.set(null);
  }
}
