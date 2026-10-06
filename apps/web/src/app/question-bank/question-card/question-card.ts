import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BookDto } from '../../book-management/book.models';
import { BookChapterPicker, NO_PLACEMENT, Placement } from '../book-chapter-picker/book-chapter-picker';
import { QuestionDto, QuestionStatus } from '../question.models';
import { QuestionReview, STATUS_LABELS } from '../question-review/question-review';
import { MathDirective } from '../../shared/rich-text/math.directive';

/**
 * One question in the question bank list (FR-5): where it is filed, its text and options with the correct one marked,
 * where it is in use, and what the author may do with it. It only shows and asks; the page holds the list and makes the
 * requests, so a card never has to know how the question got there or what happens next.
 */
@Component({
  selector: 'app-question-card',
  imports: [RouterLink, BookChapterPicker, MathDirective, QuestionReview],
  templateUrl: './question-card.html',
})
export class QuestionCard {
  readonly question = input.required<QuestionDto>();
  /** Every book, for the "File under" choice; archived ones are not offered there. */
  readonly books = input<readonly BookDto[]>([]);
  /** Whether the question is ticked for a bulk action. The page owns the selection. */
  readonly selected = input(false);
  /** True while a request about this question is running, so its buttons cannot be pressed twice. */
  readonly busy = input(false);
  /** Why the last request about this question failed, if it did. */
  readonly error = input<string | null>(null);

  /** The author confirmed deleting this question; carries its id. */
  readonly deleteConfirmed = output<string>();
  /** The author ticked or unticked the question. */
  readonly selectionChanged = output<boolean>();
  /** The question's review status changed (FR-8); carries the new one. */
  readonly statusChanged = output<{ questionId: string; status: QuestionStatus }>();
  /** The question's review status, as a person reads it. */
  protected readonly statusLabels = STATUS_LABELS;

  /** The author chose a chapter to file this question under. */
  readonly fileRequested = output<{ questionId: string; chapterId: string }>();

  protected readonly confirmingDelete = signal(false);
  protected readonly filing = signal(false);
  protected readonly placement = signal<Placement>(NO_PLACEMENT);

  /** The chapter the question was filed under when the "File under" row was last reset; undefined before the first look. */
  private shownChapterId: string | null | undefined;

  constructor() {
    // Once the question sits somewhere new, the "File under" row has done its job; close it and forget the old choice.
    effect(() => {
      const chapterId = this.question().chapterId;
      untracked(() => {
        if (chapterId !== this.shownChapterId) {
          this.shownChapterId = chapterId;
          this.filing.set(false);
          this.placement.set(NO_PLACEMENT);
        }
      });
    });
  }

  /** An exam holds the question, so the API would refuse to delete it; say so up front instead of after a click. */
  protected readonly inExam = computed(() => this.question().usage.examCount > 0);
  protected readonly examLabel = computed(() => {
    const count = this.question().usage.examCount;
    return `In ${count} ${count === 1 ? 'exam' : 'exams'}`;
  });

  protected fileHere(): void {
    const chapterId = this.placement().chapterId;
    if (chapterId) {
      this.fileRequested.emit({ questionId: this.question().id, chapterId });
    }
  }

  protected confirmDelete(): void {
    this.confirmingDelete.set(false);
    this.deleteConfirmed.emit(this.question().id);
  }
}
