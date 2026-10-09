import { Component, computed, input, output, signal } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { ShuffleRequest } from '../exam.models';

/**
 * Whether candidates see the exam's questions and options in a shuffled order (FR-12), as a small card. Like the other setting
 * cards it only collects the choice and the editor page sends it. Only a draft can change it, because the order an attempt shows
 * is worked out from these two settings every time it is read, so a later change would move questions under candidates already
 * sitting or reviewing the exam.
 */
@Component({
  selector: 'app-exam-shuffle',
  imports: [TranslatePipe],
  templateUrl: './exam-shuffle.html',
})
export class ExamShuffle {
  /** Whether questions are shuffled within each section now. */
  readonly shuffleQuestions = input.required<boolean>();
  /** Whether the options of each question are shuffled now. */
  readonly shuffleOptions = input.required<boolean>();
  /** Whether the choice can be changed; true only for a draft. */
  readonly editable = input(true);
  /** True while a request about the exam is running, so the button cannot be pressed twice. */
  readonly busy = input(false);

  /** The author saved a new choice; the page sends it. */
  readonly changed = output<ShuffleRequest>();

  /** What the author has ticked but not saved yet; null means "as the exam has it now". */
  private readonly pending = signal<ShuffleRequest | null>(null);

  protected readonly questionsTicked = computed(() => this.pending()?.shuffleQuestions ?? this.shuffleQuestions());
  protected readonly optionsTicked = computed(() => this.pending()?.shuffleOptions ?? this.shuffleOptions());
  protected readonly dirty = computed(
    () => this.questionsTicked() !== this.shuffleQuestions() || this.optionsTicked() !== this.shuffleOptions(),
  );

  protected toggleQuestions(ticked: boolean): void {
    this.pending.set({ shuffleQuestions: ticked, shuffleOptions: this.optionsTicked() });
  }

  protected toggleOptions(ticked: boolean): void {
    this.pending.set({ shuffleQuestions: this.questionsTicked(), shuffleOptions: ticked });
  }

  protected save(): void {
    if (this.dirty() && this.editable() && !this.busy()) {
      this.changed.emit({ shuffleQuestions: this.questionsTicked(), shuffleOptions: this.optionsTicked() });
    }
  }

  protected discard(): void {
    this.pending.set(null);
  }
}
