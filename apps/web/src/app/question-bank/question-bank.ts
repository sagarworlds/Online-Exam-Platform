import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { filter, take } from 'rxjs';
import { BookApiService } from '../book-management/book-api.service';
import { ANY_CLASS, bookOptionLabel, booksInClass, classChoicesOf } from '../book-management/book-class';
import { BookDto, ChapterDto } from '../book-management/book.models';
import { I18nService } from '../i18n/i18n.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { MessageKey } from '../i18n/messages.en';
import { Icon } from '../shared/icon/icon';
import { ListToolbar } from '../shared/list-shell/list-toolbar';
import { ModalDialog } from '../shared/modal-dialog/modal-dialog';
import { extractErrorMessage } from '../shared/problem-details';
import { RefreshButton } from '../shared/refresh-button/refresh-button';
import { Tooltip } from '../shared/tooltip/tooltip';
import { BookChapterPicker, NO_PLACEMENT, Placement } from './book-chapter-picker/book-chapter-picker';
import { ContentEncryptionStatusPanel } from './content-encryption-status/content-encryption-status';
import { QuestionApiService } from './question-api.service';
import { QuestionCreated, QuestionPanel } from './question-panel/question-panel';
import { QuestionRow } from './question-row/question-row';
import {
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
import { STATUS_KEYS } from './question-review/question-review';

/** The value of the list filter's book select that means "questions not filed under any chapter". */
export const UNFILED = 'unfiled';

/** The filters the list shows as removable chips, by what they narrow. */
export type FilterKey = 'class' | 'book' | 'chapter' | 'difficulty' | 'status' | 'language' | 'topic';

/**
 * Admin page: the newest questions, a line each. A question is added or edited in a panel down the side of the page (FR-5, FR-7), so the
 * list, its filters and how far it has been read stay where they are behind it. A question can be filed under a chapter of a book of a
 * class, and the list can be narrowed to a class, a book, a chapter, or what is not filed yet.
 *
 * The page keeps to one screen of what matters: a toolbar (search, filters, import and export, new question), the filters that are on as
 * chips, and the list. The filter choices are folded away until asked for, and the list is read {@link QUESTION_LIST_PAGE_SIZE} at a time.
 *
 * Editing is a child route, `/admin/questions/:id/edit`, so an edit can be linked to and the Back button closes it; the page itself is not
 * rebuilt when that route comes and goes, which is what keeps the list as it was.
 */
@Component({
  selector: 'app-question-bank',
  imports: [Icon, BookChapterPicker, ContentEncryptionStatusPanel, ListToolbar, ModalDialog, QuestionPanel, QuestionRow, QuestionTransfer, RefreshButton, Tooltip, TranslatePipe],
  templateUrl: './question-bank.html',
  styleUrl: './question-bank.css',
})
export class QuestionBank {
  private readonly api = inject(QuestionApiService);
  private readonly bookApi = inject(BookApiService);
  private readonly i18n = inject(I18nService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly unfiled = UNFILED;
  /** How many questions each read adds, for the "Load more" button's words. */
  protected readonly pageSize = QUESTION_LIST_PAGE_SIZE;
  /** Whether the panel for a new question is open. */
  protected readonly creating = signal(false);
  /** The question the panel is editing, from the address, or null. */
  protected readonly editingId = signal<string | null>(null);
  /** Whether the panel is open, to add a question or to edit one. */
  protected readonly panelOpen = computed(() => this.creating() || this.editingId() !== null);
  /** The language a new question is written in, kept between panels: an author enters many in one language in a row. */
  protected readonly language = signal<QuestionLanguage>('en');
  /** Whether the filter choices are showing. The ones that are on show as chips whether or not they are. */
  protected readonly filtersOpen = signal(false);
  /** Whether the import and export dialog is open. */
  protected readonly transferOpen = signal(false);
  protected readonly questions = signal<QuestionDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  /** Whether the list is being read again because the refresh icon was pressed; the questions stay until the answer comes. */
  protected readonly refreshing = signal(false);
  /** Counts the reads of the first page, so the answer to a refresh is dropped if another read (a filter chosen meanwhile) started after it. */
  private latestRead = 0;
  /** Whether the last page came back full, so there may be older questions to load. */
  protected readonly mayHaveMore = signal(false);
  /** Why the page could not load something: the list, the books or the topics. */
  protected readonly errorMessage = signal<string | null>(null);
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
   * Where a new question is filed. A book needs a chapter, because a question belongs to a chapter, not a book.
   * It is kept here, not in the panel, which is a new one each time: an author enters many questions into one chapter in a row.
   */
  protected readonly placement = signal<Placement>(NO_PLACEMENT);

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
  protected readonly filterChapters = computed(() => this.books().find((book) => book.id === this.filterBook())?.chapters ?? []);

  /** The filters that are on, as the words a chip shows. The search is not one: it has its own box, which says what it is. */
  protected readonly activeFilters = computed<{ key: FilterKey; label: string }[]>(() => {
    const chips: { key: FilterKey; label: string }[] = [];
    const classId = this.filterClass();
    if (classId) {
      const name = this.filterClasses().find((item) => item.id === classId)?.name ?? this.i18n.t('qb.chip.unknown');
      chips.push({ key: 'class', label: this.i18n.t('qb.chip.class', { value: name }) });
    }

    const bookId = this.filterBook();
    if (bookId === UNFILED) {
      chips.push({ key: 'book', label: this.i18n.t('qb.unfiled') });
    } else if (bookId) {
      const name = this.books().find((book) => book.id === bookId)?.name ?? this.i18n.t('qb.chip.unknown');
      chips.push({ key: 'book', label: this.i18n.t('qb.chip.book', { value: name }) });
      const chapter = this.filterChapters().find((item) => item.id === this.filterChapter());
      if (chapter) {
        chips.push({ key: 'chapter', label: this.i18n.t('qb.chip.chapter', { value: `${chapter.order}. ${chapter.title}` }) });
      }
    }

    const difficulty = this.difficulties.find((level) => level.value === this.filterDifficulty());
    if (difficulty) {
      chips.push({ key: 'difficulty', label: this.i18n.t('qb.chip.difficulty', { value: this.i18n.t(difficulty.key) }) });
    }
    const status = this.statuses.find((item) => item.value === this.filterStatus());
    if (status) {
      chips.push({ key: 'status', label: this.i18n.t('qb.chip.status', { value: this.i18n.t(status.key) }) });
    }
    const language = this.languages.find((item) => item.code === this.filterLanguage());
    if (language) {
      chips.push({ key: 'language', label: this.i18n.t('qb.chip.language', { value: this.i18n.t(language.key) }) });
    }
    if (this.filterTopic()) {
      chips.push({ key: 'topic', label: this.i18n.t('qb.chip.topic', { value: this.filterTopic() }) });
    }
    return chips;
  });

  /** What a book is called in the filter's book select: with its class while every class is listed, and marked when archived. */
  protected bookOption(book: BookDto): string {
    const label = bookOptionLabel(book, this.filterClass());
    return book.isArchived ? `${label} ${this.i18n.t('qb.archivedSuffix')}` : label;
  }

  /** What a chapter is called in the filter's chapter select. */
  protected chapterOption(chapter: ChapterDto): string {
    const label = `${chapter.order}. ${chapter.title}`;
    return chapter.isArchived ? `${label} ${this.i18n.t('qb.archivedSuffix')}` : label;
  }

  /** What the button that files the ticked questions says. */
  protected fileSelectedText(): string {
    return this.i18n.plural('qb.bulk.file', this.selectedIds().size);
  }

  constructor() {
    this.refresh();
    this.loadTopics();
    this.loadBooks();

    // The edit panel follows the address: opened by a link to it or the Edit icon, closed by Back as well as by its own buttons.
    this.editingId.set(this.editedQuestionId());
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.editingId.set(this.editedQuestionId()));
  }

  /**
   * Reads the list again, with the books and topics it is filtered by and the statistics of the questions that were opened, so questions
   * added or changed by others show without reloading the tab. The list is not blanked, and the filters, the search, the ticked questions
   * and an open panel are kept. The list starts again from its first page, and "Load more" continues from there. A failed read leaves what
   * is shown and says why.
   */
  protected onRefresh(): void {
    if (this.refreshing()) {
      return;
    }

    this.errorMessage.set(null);
    this.refresh(true);
    this.loadTopics();
    this.loadBooks();
  }

  private loadBooks(): void {
    this.bookApi.list(true).subscribe({
      next: (books) => this.books.set(books),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong'))),
    });
  }

  /**
   * The question the address names to edit, if it does. Read from the child route's parameters as an observable that answers at once:
   * while this page is being built for a link straight to an edit, that route has no `snapshot` yet, but it already has its parameters.
   */
  private editedQuestionId(): string | null {
    let id: string | null = null;
    this.route.firstChild?.paramMap.pipe(take(1)).subscribe((params) => (id = params.get('id')));
    return id;
  }

  protected openNewQuestion(): void {
    this.creating.set(true);
  }

  /** The panel closed, after a save or without one. An edit's address goes back to the list; the list itself was never rebuilt. */
  protected onPanelClosed(): void {
    this.creating.set(false);
    if (this.editingId() !== null) {
      void this.router.navigate(['/admin/questions']);
    }
  }

  /**
   * A question was added: the list is read again for it, and its topics offered. Said here only when the panel closes with it, as one that
   * stays open for the next question says so itself.
   */
  protected onCreated(created: QuestionCreated): void {
    this.notice.set(created.another ? null : this.i18n.t('qb.saved'));
    this.refresh();
    // The new question may have brought a topic nobody used before.
    this.loadTopics();
  }

  /** A question was edited: its line shows it as it is now, without reading the whole list again. */
  protected onEdited(updated: QuestionDto): void {
    this.questions.update((list) => list.map((question) => (question.id === updated.id ? updated : question)));
    this.notice.set(this.i18n.t('qb.saved'));
    this.loadTopics();
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

  protected readonly statuses: readonly { value: QuestionStatus; key: MessageKey }[] = (Object.keys(STATUS_KEYS) as QuestionStatus[]).map((value) => ({
    value,
    key: STATUS_KEYS[value],
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

  /** Takes one filter off, from its chip. Taking a book off takes its chapter with it, as choosing another book does. */
  protected clearFilter(key: FilterKey): void {
    switch (key) {
      case 'class':
        this.onFilterClassChanged(ANY_CLASS);
        break;
      case 'book':
        this.onFilterBookChanged('');
        break;
      case 'chapter':
        this.onFilterChapterChanged('');
        break;
      case 'difficulty':
        this.onFilterDifficultyChanged('');
        break;
      case 'status':
        this.onFilterStatusChanged('');
        break;
      case 'language':
        this.onFilterLanguageChanged('');
        break;
      case 'topic':
        this.onFilterTopicChanged('');
        break;
    }
  }

  /** Takes every filter off at once, with one read of the list. The search is not a filter chip, so it stays. */
  protected clearAllFilters(): void {
    this.filterClass.set(ANY_CLASS);
    this.filterBook.set('');
    this.filterChapter.set('');
    this.filterDifficulty.set('');
    this.filterStatus.set('');
    this.filterLanguage.set('');
    this.filterTopic.set('');
    this.refresh();
  }

  /** Loads how candidates did on a question, for its card. */
  protected onStatisticsRequested(questionId: string): void {
    this.api.statistics(questionId).subscribe({
      next: (stats) => this.statistics.update((all) => ({ ...all, [questionId]: stats })),
      error: (error: unknown) => this.failAction(questionId, error),
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
        this.bulkError.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
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
        this.notice.set(this.i18n.t('qb.deleted'));
      },
      error: (error: unknown) => this.failAction(id, error),
    });
  }

  private announceFiled(result: FileQuestionsResult): void {
    const where = `${result.bookName} › ${result.chapterTitle}`;
    this.notice.set(result.moved === 0 ? this.i18n.t('qb.filedNothing', { where }) : this.i18n.plural('qb.filed', result.moved, { where }));
  }

  private startAction(id: string): void {
    this.busyId.set(id);
    this.notice.set(null);
    this.cardErrors.update((errors) => Object.fromEntries(Object.entries(errors).filter(([key]) => key !== id)));
  }

  private failAction(id: string, error: unknown): void {
    this.busyId.set(null);
    this.cardErrors.update((errors) => ({ ...errors, [id]: extractErrorMessage(error, this.i18n.t('common.somethingWrong')) }));
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
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong'))),
    });
  }

  /** Adds the next page of older questions below the ones shown. Ones already shown are not repeated, whatever was created meanwhile. */
  protected loadMore(): void {
    if (this.loadingMore() || !this.mayHaveMore()) {
      return;
    }

    this.loadingMore.set(true);
    this.api.list(this.currentFilter(), this.questions().length, QUESTION_LIST_PAGE_SIZE).subscribe({
      next: (page) => {
        const shown = new Set(this.questions().map((q) => q.id));
        this.questions.update((list) => [...list, ...page.filter((q) => !shown.has(q.id))]);
        this.mayHaveMore.set(page.length >= QUESTION_LIST_PAGE_SIZE);
        this.loadingMore.set(false);
      },
      error: (error: unknown) => {
        this.loadingMore.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /** Reads the first page of the list. A quiet read, for the refresh icon, does not blank the list while it is on its way. */
  private refresh(quiet = false): void {
    this.loading.set(!quiet);
    this.refreshing.set(quiet);
    const read = ++this.latestRead;
    this.api.list(this.currentFilter(), 0, QUESTION_LIST_PAGE_SIZE).subscribe({
      next: (questions) => {
        // The refresh was for a list that is no longer the one chosen: its answer would put the wrong questions on screen.
        if (quiet && read !== this.latestRead) {
          return;
        }

        this.questions.set(questions);
        this.mayHaveMore.set(questions.length >= QUESTION_LIST_PAGE_SIZE);
        // Only what is still in the list can stay ticked: a filter change or a filing may have taken questions out of it.
        const present = new Set(questions.map((q) => q.id));
        this.selectedIds.update((current) => new Set([...current].filter((id) => present.has(id))));
        if (quiet) {
          this.refreshStatistics(present);
        }
        this.loading.set(false);
        this.refreshing.set(false);
      },
      error: (error: unknown) => {
        if (quiet && read !== this.latestRead) {
          return;
        }

        this.loading.set(false);
        this.refreshing.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /** Reads again the statistics of the questions whose cards were opened and are still listed; those no longer listed are forgotten. */
  private refreshStatistics(present: ReadonlySet<string>): void {
    const opened = Object.keys(this.statistics()).filter((id) => present.has(id));
    this.statistics.update((all) => Object.fromEntries(Object.entries(all).filter(([id]) => present.has(id))));
    for (const id of opened) {
      this.api.statistics(id).subscribe({
        next: (stats) => this.statistics.update((all) => ({ ...all, [id]: stats })),
        error: (error: unknown) => this.failAction(id, error),
      });
    }
  }
}
