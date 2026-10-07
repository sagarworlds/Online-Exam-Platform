import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BookDto } from '../../book-management/book.models';
import { BookChapterPicker, NO_PLACEMENT, Placement } from '../book-chapter-picker/book-chapter-picker';
import { QUESTION_LANGUAGES, QuestionDto, QuestionStatistics, QuestionStatus } from '../question.models';
import { QuestionTranslations } from '../question-translations/question-translations';
import { QuestionReview, STATUS_LABELS } from '../question-review/question-review';
import { MathDirective } from '../../shared/rich-text/math.directive';

/**
 * One question in the question bank list (FR-5): where it is filed, its text and options with the correct one marked,
 * where it is in use, and what the author may do with it. It only shows and asks; the page holds the list and makes the
 * requests, so a card never has to know how the question got there or what happens next.
 */
@Component({
  selector: 'app-question-card',
  imports: [RouterLink, BookChapterPicker, MathDirective, QuestionReview, QuestionTranslations],
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

  /** How candidates did on this question, once the page has loaded it (FR-9). */
  readonly statistics = input<QuestionStatistics | null>(null);

  /** The author opened the statistics before they were loaded; carries the question's id. */
  readonly statisticsRequested = output<string>();
  protected readonly showingStatistics = signal(false);

  /** A translation of this question was saved (FR-10). */
  readonly translationAdded = output<void>();
  protected readonly showingTranslations = signal(false);

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

  /** The question's language as an author reads it, or null for a question from an API that predates languages. */
  protected readonly languageLabel = computed(() => QUESTION_LANGUAGES.find((language) => language.code === this.question().language)?.label ?? null);

  protected toggleStatistics(): void {
    const open = !this.showingStatistics();
    this.showingStatistics.set(open);
    if (open && this.statistics() === null) {
      this.statisticsRequested.emit(this.question().id);
    }
  }

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
