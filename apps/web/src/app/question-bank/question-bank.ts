import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { BookApiService } from '../book-management/book-api.service';
import { ANY_CLASS, bookOptionLabel, booksInClass, classChoicesOf } from '../book-management/book-class';
import { BookDto } from '../book-management/book.models';
import { extractErrorMessage, extractProblemCode } from '../shared/problem-details';
import { BookChapterPicker, isCompletePlacement, NO_PLACEMENT, Placement } from './book-chapter-picker/book-chapter-picker';
import { QuestionApiService } from './question-api.service';
import { QuestionCard } from './question-card/question-card';
import {
  createQuestionForm,
  newOption,
  setAcceptedAnswers,
  toAcceptedAnswers,
  toAllowsMultiple,
  toExplanation,
  toIsTextAnswer,
  toLabels,
  toNewOptions,
} from './question-form';
import { QuestionFields } from './question-fields/question-fields';
import {
  CreateQuestionRequest,
  DuplicateQuestion,
  FileQuestionsResult,
  QUESTION_DIFFICULTIES,
  QUESTION_LANGUAGES,
  QUESTION_LIST_PAGE_SIZE,
  QuestionDifficulty,
  QuestionDto,
  QuestionFilter,
  QuestionLanguage,
  QuestionStatistics,
  QuestionStatus,
} from './question.models';
import { QuestionTransfer } from './question-transfer/question-transfer';
import { STATUS_LABELS } from './question-review/question-review';

/** The value of the list filter's book select that means "questions not filed under any chapter". */
export const UNFILED = 'unfiled';

/**
 * Admin page: the newest questions, and a form to add one with its options and correct answer (FR-5). A question can
 * be filed under a chapter of a book of a class, and the list can be narrowed to a class, a book, a chapter, or what is not filed yet.
 */
@Component({
  selector: 'app-question-bank',
  imports: [ReactiveFormsModule, BookChapterPicker, QuestionCard, QuestionFields, QuestionTransfer],
  templateUrl: './question-bank.html',
})
export class QuestionBank {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(QuestionApiService);
  private readonly bookApi = inject(BookApiService);

  protected readonly unfiled = UNFILED;
  protected readonly questions = signal<QuestionDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  /** Whether the last page came back full, so there may be older questions to load. */
  protected readonly mayHaveMore = signal(false);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** A question the bank refused as a repeat, with what it repeats, until the author adds it anyway or gives up (FR-9). */
  protected readonly repeat = signal<{ request: CreateQuestionRequest; matches: DuplicateQuestion[] } | null>(null);
  /** What candidates did on the questions whose statistics were opened, by question id (FR-9). */
  protected readonly statistics = signal<Record<string, QuestionStatistics>>({});
  /** What the last action on the list did, such as a deletion; announced to screen readers. */
  protected readonly notice = signal<string | null>(null);
  /** The question a request is running for, if any, so only its card is locked meanwhile. */
  protected readonly busyId = signal<string | null>(null);
  /** Why the last request about a question failed, by question id, so the reason shows on that question's own card. */
  protected readonly cardErrors = signal<Record<string, string>>({});

  /** The questions ticked for a bulk action, by id. */
  protected readonly selectedIds = signal<ReadonlySet<string>>(new Set());
  protected readonly allSelected = computed(() => this.questions().length > 0 && this.questions().every((q) => this.selectedIds().has(q.id)));
  /** Where the selected questions will be filed. */
  protected readonly bulkPlacement = signal<Placement>(NO_PLACEMENT);
  protected readonly bulkBusy = signal(false);
  protected readonly bulkError = signal<string | null>(null);

  /** Every book, archived ones included: the list filter must reach questions filed under them. */
  protected readonly books = signal<BookDto[]>([]);

  /**
   * Where the new question is filed. A book needs a chapter, because a question belongs to a chapter, not a book.
   * It survives saving: an author enters many questions into one chapter in a row.
   */
  protected readonly placement = signal<Placement>(NO_PLACEMENT);
  protected readonly placementComplete = computed(() => isCompletePlacement(this.placement()));

  /** The list filter: a class id or '' for every class; a book id, {@link UNFILED}, or '' for everything; and a chapter id or ''. */
  protected readonly filterClass = signal(ANY_CLASS);
  protected readonly filterBook = signal('');
  protected readonly filterChapter = signal('');
  /** The difficulty and topic the list is narrowed to, '' for any. */
  protected readonly filterDifficulty = signal<QuestionDifficulty | ''>('');
  protected readonly filterTopic = signal('');
  /** The text the list is searched for; empty for no search. */
  protected readonly filterSearch = signal('');
  protected readonly filterStatus = signal<QuestionStatus | ''>('');
  /** The language the list is narrowed to, '' for any (FR-10). */
  protected readonly filterLanguage = signal<QuestionLanguage | ''>('');
  protected readonly languages = QUESTION_LANGUAGES;
  protected readonly difficulties = QUESTION_DIFFICULTIES;
  /** Every topic in use, for the topic filter and for the form's suggestions. */
  protected readonly topics = signal<string[]>([]);
  /** The classes the books are filed under, archived books included: a class whose books are all archived still has questions. */
  protected readonly filterClasses = computed(() => classChoicesOf(this.books()));
  /** The books the book filter offers: those of the chosen class, or every book. */
  protected readonly filterBooks = computed(() => booksInClass(this.books(), this.filterClass()));
  protected readonly optionLabel = bookOptionLabel;
  protected readonly filterChapters = computed(() => this.books().find((book) => book.id === this.filterBook())?.chapters ?? []);

  protected readonly form = createQuestionForm(this.formBuilder);

  constructor() {
    this.refresh();
    this.loadTopics();
    this.bookApi.list(true).subscribe({
      next: (books) => this.books.set(books),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }

  protected onFilterClassChanged(value: string): void {
    this.filterClass.set(value);
    // A book belongs to one class, and a question not filed under a chapter to none, so a book or chapter filter that no
    // longer matches the class is cleared rather than left to match nothing.
    const book = this.books().find((candidate) => candidate.id === this.filterBook());
    if (value !== ANY_CLASS && (this.filterBook() === UNFILED || (book !== undefined && book.classId !== value))) {
      this.filterBook.set('');
      this.filterChapter.set('');
    }
    this.refresh();
  }

  protected onFilterBookChanged(value: string): void {
    this.filterBook.set(value);
    this.filterChapter.set('');
    this.refresh();
  }

  protected onFilterChapterChanged(value: string): void {
    this.filterChapter.set(value);
    this.refresh();
  }

  protected readonly statuses: readonly { value: QuestionStatus; label: string }[] = (Object.keys(STATUS_LABELS) as QuestionStatus[]).map((value) => ({
    value,
    label: STATUS_LABELS[value],
  }));

  protected onFilterStatusChanged(value: string): void {
    this.filterStatus.set(value as QuestionStatus | '');
    this.refresh();
  }

  /** A question's review status changed: show it on its card without reading the whole list again. */
  protected onStatusChanged(change: { questionId: string; status: QuestionStatus }): void {
    this.questions.update((list) => list.map((q) => (q.id === change.questionId ? { ...q, status: change.status } : q)));
  }

  protected onFilterDifficultyChanged(value: string): void {
    this.filterDifficulty.set(value as QuestionDifficulty | '');
    this.refresh();
  }

  /**
   * Searches the list for the typed text. Both Enter and leaving the field call this, so it does nothing when the text is the one
   * already searched for, and one search is never sent twice.
   */
  protected onSearch(value: string): void {
    const text = value.trim();
    if (text === this.filterSearch()) {
      return;
    }

    this.filterSearch.set(text);
    this.refresh();
  }

  protected onFilterLanguageChanged(value: string): void {
    this.filterLanguage.set(value as QuestionLanguage | '');
    this.refresh();
  }

  /** A translation was added from a card: it is a question of its own, so the list is read again. */
  protected onTranslationAdded(): void {
    this.refresh();
  }

  protected onFilterTopicChanged(value: string): void {
    this.filterTopic.set(value);
    this.refresh();
  }

  protected submit(): void {
    if (this.form.invalid || !this.placementComplete() || this.saving()) {
      return;
    }

    const request: CreateQuestionRequest = {
      text: this.form.getRawValue().text,
      chapterId: this.placement().chapterId || null,
      options: toNewOptions(this.form),
      ...toLabels(this.form),
      allowsMultiple: toAllowsMultiple(this.form),
      language: this.form.getRawValue().language,
      isTextAnswer: toIsTextAnswer(this.form),
      acceptedAnswers: toAcceptedAnswers(this.form),
      explanation: toExplanation(this.form),
    };

    this.send(request);
  }

  /** Adds the question the bank called a repeat, because the author looked at what it repeats and still wants it. */
  protected addAnyway(): void {
    const pending = this.repeat();
    if (pending && !this.saving()) {
      this.send({ ...pending.request, allowDuplicate: true });
    }
  }

  protected dismissRepeat(): void {
    this.repeat.set(null);
  }

  /** Loads how candidates did on a question, for its card. */
  protected onStatisticsRequested(questionId: string): void {
    this.api.statistics(questionId).subscribe({
      next: (stats) => this.statistics.update((all) => ({ ...all, [questionId]: stats })),
      error: (error: unknown) => this.failAction(questionId, error),
    });
  }

  private send(request: CreateQuestionRequest): void {
    this.saving.set(true);
    this.saved.set(false);
    this.errorMessage.set(null);
    this.repeat.set(null);

    this.api.create(request).subscribe({
      next: () => {
        this.saving.set(false);
        this.saved.set(true);
        this.resetForm();
        this.refresh();
        // The new question may have brought a topic nobody used before.
        this.loadTopics();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        if (extractProblemCode(error) === 'duplicate_question') {
          this.showRepeat(request, error);
          return;
        }
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  /** Looks up what the refused question repeats, so the author can see it before deciding. */
  private showRepeat(request: CreateQuestionRequest, refusal: unknown): void {
    this.api.duplicates(request.text, request.options.map((o) => o.text)).subscribe({
      next: (matches) => this.repeat.set({ request, matches }),
      // The refusal itself is the message when the list of matches cannot be had.
      error: () => this.errorMessage.set(extractErrorMessage(refusal)),
    });
  }

  protected toggleSelected(id: string, selected: boolean): void {
    this.selectedIds.update((current) => {
      const next = new Set(current);
      if (selected) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  protected toggleAll(selected: boolean): void {
    this.selectedIds.set(selected ? new Set(this.questions().map((q) => q.id)) : new Set());
  }

  /** Files every ticked question under the chosen chapter, all or none; the API refuses the lot if any one may not move. */
  protected fileSelected(): void {
    const chapterId = this.bulkPlacement().chapterId;
    if (!chapterId || this.bulkBusy()) {
      return;
    }

    this.bulkBusy.set(true);
    this.bulkError.set(null);
    this.notice.set(null);
    this.api.file({ questionIds: [...this.selectedIds()], chapterId }).subscribe({
      next: (result) => {
        this.bulkBusy.set(false);
        this.selectedIds.set(new Set());
        this.announceFiled(result);
        this.refresh();
      },
      error: (error: unknown) => {
        this.bulkBusy.set(false);
        this.bulkError.set(extractErrorMessage(error));
      },
    });
  }

  protected fileQuestion(questionId: string, chapterId: string): void {
    this.startAction(questionId);
    this.api.file({ questionIds: [questionId], chapterId }).subscribe({
      next: (result) => {
        this.busyId.set(null);
        this.announceFiled(result);
        // The list is read again, not patched: under a filter, a question that has moved no longer belongs in it.
        this.refresh();
      },
      error: (error: unknown) => this.failAction(questionId, error),
    });
  }

  protected deleteQuestion(id: string): void {
    this.startAction(id);
    this.api.remove(id).subscribe({
      next: () => {
        this.busyId.set(null);
        this.questions.update((list) => list.filter((question) => question.id !== id));
        this.selectedIds.update((current) => new Set([...current].filter((selectedId) => selectedId !== id)));
        this.notice.set('Question deleted.');
      },
      error: (error: unknown) => this.failAction(id, error),
    });
  }

  private announceFiled(result: FileQuestionsResult): void {
    const where = `${result.bookName} › ${result.chapterTitle}`;
    this.notice.set(
      result.moved === 0
        ? `Nothing to move: those questions are already in ${where}.`
        : `Filed ${result.moved} ${result.moved === 1 ? 'question' : 'questions'} under ${where}.`,
    );
  }

  private startAction(id: string): void {
    this.busyId.set(id);
    this.notice.set(null);
    this.saved.set(false);
    this.cardErrors.update((errors) => Object.fromEntries(Object.entries(errors).filter(([key]) => key !== id)));
  }

  private failAction(id: string, error: unknown): void {
    this.busyId.set(null);
    this.cardErrors.update((errors) => ({ ...errors, [id]: extractErrorMessage(error) }));
  }

  /** The filter the list is currently narrowed by; a chapter implies its book, so only one of them is sent. The class is sent besides them, since it narrows too. */
  private currentFilter(): QuestionFilter {
    return { ...(this.filterClass() ? { classId: this.filterClass() } : {}), ...this.placeFilter(), ...this.labelFilter() };
  }

  private placeFilter(): QuestionFilter {
    const book = this.filterBook();
    if (book === UNFILED) {
      return { unfiled: true };
    }
    if (this.filterChapter()) {
      return { chapterId: this.filterChapter() };
    }
    return book ? { bookId: book } : {};
  }

  private labelFilter(): QuestionFilter {
    const difficulty = this.filterDifficulty();
    const topic = this.filterTopic();
    const search = this.filterSearch();
    const status = this.filterStatus();
    const language = this.filterLanguage();
    return {
      ...(difficulty ? { difficulty } : {}),
      ...(topic ? { topic } : {}),
      ...(search ? { search } : {}),
      ...(status ? { status } : {}),
      ...(language ? { language } : {}),
    };
  }

  /** What the export button downloads: the questions the list is showing. */
  protected readonly exportFilter = computed(() => this.currentFilter());

  /** An import created questions: show them, and offer their topics. */
  protected onImported(): void {
    this.refresh();
    this.loadTopics();
  }

  private loadTopics(): void {
    this.api.topics().subscribe({
      next: (topics) => this.topics.set(topics),
      // Suggestions and the topic filter are conveniences; a failure here must not stop the page, but it is not hidden.
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }

  /** Adds the next page of older questions below the ones shown. Ones already shown are not repeated, whatever was created meanwhile. */
  protected loadMore(): void {
    if (this.loadingMore() || !this.mayHaveMore()) {
      return;
    }

    this.loadingMore.set(true);
    this.api.list(this.currentFilter(), this.questions().length).subscribe({
      next: (page) => {
        const shown = new Set(this.questions().map((q) => q.id));
        this.questions.update((list) => [...list, ...page.filter((q) => !shown.has(q.id))]);
        this.mayHaveMore.set(page.length >= QUESTION_LIST_PAGE_SIZE);
        this.loadingMore.set(false);
      },
      error: (error: unknown) => {
        this.loadingMore.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  private refresh(): void {
    this.loading.set(true);
    this.api.list(this.currentFilter()).subscribe({
      next: (questions) => {
        this.questions.set(questions);
        this.mayHaveMore.set(questions.length >= QUESTION_LIST_PAGE_SIZE);
        // Only what is still in the list can stay ticked: a filter change or a filing may have taken questions out of it.
        const present = new Set(questions.map((q) => q.id));
        this.selectedIds.update((current) => new Set([...current].filter((id) => present.has(id))));
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  private resetForm(): void {
    // The language stays as it was: an author enters many questions in one language in a row.
    this.form.reset({
      text: '',
      questionType: 'choice',
      allowsMultiple: false,
      correctIndex: -1,
      difficulty: '',
      topics: '',
      language: this.form.getRawValue().language,
    });
    this.form.controls.options.clear();
    this.form.controls.options.push(newOption(this.formBuilder));
    this.form.controls.options.push(newOption(this.formBuilder));
    setAcceptedAnswers(this.form, this.formBuilder);
  }
}
