import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { BookApiService } from '../book-management/book-api.service';
import { BookDto } from '../book-management/book.models';
import { extractErrorMessage } from '../shared/problem-details';
import { BookChapterPicker, isCompletePlacement, NO_PLACEMENT, Placement } from './book-chapter-picker/book-chapter-picker';
import { QuestionApiService } from './question-api.service';
import { QuestionCard } from './question-card/question-card';
import { createQuestionForm, newOption, toAllowsMultiple, toLabels, toNewOptions } from './question-form';
import { QuestionFields } from './question-fields/question-fields';
import {
  CreateQuestionRequest,
  FileQuestionsResult,
  QUESTION_DIFFICULTIES,
  QUESTION_LIST_PAGE_SIZE,
  QuestionDifficulty,
  QuestionDto,
  QuestionFilter,
} from './question.models';

/** The value of the list filter's book select that means "questions not filed under any chapter". */
export const UNFILED = 'unfiled';

/**
 * Admin page: the newest questions, and a form to add one with its options and correct answer (FR-5). A question can
 * be filed under a chapter of a book, and the list can be narrowed to a book, a chapter, or what is not filed yet.
 */
@Component({
  selector: 'app-question-bank',
  imports: [ReactiveFormsModule, BookChapterPicker, QuestionCard, QuestionFields],
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

  /** The list filter: a book id, {@link UNFILED}, or '' for everything; and a chapter id or ''. */
  protected readonly filterBook = signal('');
  protected readonly filterChapter = signal('');
  /** The difficulty and topic the list is narrowed to, '' for any. */
  protected readonly filterDifficulty = signal<QuestionDifficulty | ''>('');
  protected readonly filterTopic = signal('');
  /** The text the list is searched for; empty for no search. */
  protected readonly filterSearch = signal('');
  protected readonly difficulties = QUESTION_DIFFICULTIES;
  /** Every topic in use, for the topic filter and for the form's suggestions. */
  protected readonly topics = signal<string[]>([]);
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

  protected onFilterBookChanged(value: string): void {
    this.filterBook.set(value);
    this.filterChapter.set('');
    this.refresh();
  }

  protected onFilterChapterChanged(value: string): void {
    this.filterChapter.set(value);
    this.refresh();
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
    };

    this.saving.set(true);
    this.saved.set(false);
    this.errorMessage.set(null);

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
        this.errorMessage.set(extractErrorMessage(error));
      },
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

  /** The filter the list is currently narrowed by; a chapter implies its book, so only one of them is sent. */
  private currentFilter(): QuestionFilter {
    return { ...this.placeFilter(), ...this.labelFilter() };
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
    return { ...(difficulty ? { difficulty } : {}), ...(topic ? { topic } : {}), ...(search ? { search } : {}) };
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
    this.form.reset({ text: '', allowsMultiple: false, correctIndex: -1, difficulty: '', topics: '' });
    this.form.controls.options.clear();
    this.form.controls.options.push(newOption(this.formBuilder));
    this.form.controls.options.push(newOption(this.formBuilder));
  }
}
