import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BookDto } from '../../book-management/book.models';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { Icon } from '../../shared/icon/icon';
import { MathDirective } from '../../shared/rich-text/math.directive';
import { Tooltip } from '../../shared/tooltip/tooltip';
import { BookChapterPicker, NO_PLACEMENT, Placement } from '../book-chapter-picker/book-chapter-picker';
import { QUESTION_DIFFICULTIES, QUESTION_LANGUAGES, QuestionDto, OptionStatistics, QuestionStatistics, QuestionStatus } from '../question.models';
import { QuestionTranslations } from '../question-translations/question-translations';
import { QuestionReview, STATUS_KEYS } from '../question-review/question-review';
import { MathTextDirective } from '../../shared/rich-text/math-text.directive';

/**
 * One question in the question bank list (FR-5), as a line: its text (two lines of it), where it is filed, its language, review status and
 * difficulty, and the icons for what the author may do with it. The chevron opens it to show the options with the correct one marked, the
 * topics, and the tools that need room (statistics, translations, filing and the review thread). It only shows and asks; the page holds the
 * list and makes the requests, so a row never has to know how the question got there or what happens next.
 *
 * A refusal and the "delete for good?" question are shown on the line itself, open or not, so a closed row can never hide them.
 */
@Component({
  selector: 'app-question-row',
  imports: [RouterLink, Icon, BookChapterPicker, MathDirective, QuestionReview, QuestionTranslations, Tooltip, TranslatePipe, MathTextDirective],
  templateUrl: './question-row.html',
  styleUrl: './question-row.css',
  host: { role: 'listitem' },
})
export class QuestionRow {
  private readonly i18n = inject(I18nService);

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
  /** The messages that say the question's review status as a person reads it. */
  protected readonly statusKeys = STATUS_KEYS;

  /** The author chose a chapter to file this question under. */
  readonly fileRequested = output<{ questionId: string; chapterId: string }>();

  /** Whether the row is open, showing its options and tools. */
  protected readonly open = signal(false);
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

  /** The id the open part has, so its chevron can name it. */
  protected readonly detailId = computed(() => `question-detail-${this.question().id}`);

  /** An exam holds the question, so the API would refuse to delete it; say so up front instead of after a click. */
  protected readonly inExam = computed(() => this.question().usage.examCount > 0);
  protected readonly examLabel = computed(() => this.i18n.plural('qb.row.inExams', this.question().usage.examCount));

  /** The question's language as an author reads it, or null for a question from an API that predates languages. */
  protected readonly languageLabel = computed(() => {
    const entry = QUESTION_LANGUAGES.find((language) => language.code === this.question().language);
    return entry ? this.i18n.t(entry.key) : null;
  });

  /** How hard the question is as an author reads it, or null when nobody said. */
  protected readonly difficultyLabel = computed(() => {
    const entry = QUESTION_DIFFICULTIES.find((level) => level.value === this.question().difficulty);
    return entry ? this.i18n.t(entry.key) : null;
  });

  /** What kind of question it is, for the icon in front of its text. */
  protected readonly typeLabel = computed(() => {
    const question = this.question();
    if (question.isTextAnswer) {
      return this.i18n.t('question.type.text');
    }
    return this.i18n.t(question.allowsMultiple ? 'qb.row.typeMultiple' : 'qb.row.typeSingle');
  });

  /** How many times the question was answered and how many of those were fully right. */
  protected answeredText(stats: QuestionStatistics): string {
    return this.i18n.plural('qb.row.answeredTimes', stats.answered, { correct: stats.correct, percent: stats.percentCorrect ?? 0 });
  }

  /** How many times an option was chosen, and that it is the right one if it is. */
  protected chosenText(option: OptionStatistics): string {
    return this.i18n.plural(option.isCorrect ? 'qb.row.chosenCorrect' : 'qb.row.chosen', option.timesChosen, { text: option.text });
  }

  protected toggleOpen(): void {
    this.open.update((open) => !open);
  }

  protected toggleStatistics(): void {
    const showing = !this.showingStatistics();
    this.showingStatistics.set(showing);
    if (showing) {
      this.open.set(true);
      if (this.statistics() === null) {
        this.statisticsRequested.emit(this.question().id);
      }
    }
  }

  protected toggleTranslations(): void {
    const showing = !this.showingTranslations();
    this.showingTranslations.set(showing);
    if (showing) {
      this.open.set(true);
    }
  }

  protected toggleFiling(): void {
    const showing = !this.filing();
    this.filing.set(showing);
    if (showing) {
      this.open.set(true);
    }
  }

  protected fileHere(): void {
    const chapterId = this.placement().chapterId;
    if (chapterId) {
      this.fileRequested.emit({ questionId: this.question().id, chapterId });
    }
  }

  /** Asks "delete for good?". A question an exam holds is not asked about: its icon is unavailable and says why. */
  protected askDelete(): void {
    if (!this.inExam()) {
      this.confirmingDelete.set(true);
    }
  }

  protected confirmDelete(): void {
    this.confirmingDelete.set(false);
    this.deleteConfirmed.emit(this.question().id);
  }
}
