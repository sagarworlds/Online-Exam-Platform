import { Component, ElementRef, Injector, afterNextRender, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Observable, Subject, catchError, debounceTime, map, merge, switchMap, tap } from 'rxjs';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { QuestionApiService } from '../../question-bank/question-api.service';
import { QUESTION_DIFFICULTIES, QuestionDifficulty, QuestionDto } from '../../question-bank/question.models';
import { FieldControl, FormField } from '../../shared/form-field/form-field';
import { htmlToPlainText } from '../../shared/rich-text/html-to-text';
import { ExamScopeDto } from '../exam.models';
import { isInScope } from './in-scope';

/** How many questions are listed at a time; more are asked for with "Show more". */
const PAGE_SIZE = 20;

/** How long after the last key press the search is sent. */
const SEARCH_DELAY_MS = 300;

/**
 * Where an author finds a question in the bank to put into a section: a search box, a difficulty, and the questions that match, each with
 * an Add button. It asks the question bank itself, a page at a time, so a bank of any size can be searched; the list this replaced was the
 * newest two hundred questions in a drop-down, cut off to a line each, with no way to reach the rest.
 *
 * It lists only what the exam will accept: questions already in the exam are left out (a question goes in once) and so are those outside its
 * book or chapters, which the API would refuse. A question added is handed to the page with `picked`; the page reads the exam again, and
 * `excluded` then drops the question from the list. Every read, the first, a new search and "Show more", goes through one stream, so a newer
 * one cancels one still on its way and a late answer can never land in a list it does not belong to.
 */
@Component({
  selector: 'app-question-picker',
  imports: [FormField, FieldControl, TranslatePipe],
  templateUrl: './question-picker.html',
  styleUrl: './question-picker.css',
})
export class QuestionPicker {
  private readonly questionApi = inject(QuestionApiService);
  private readonly i18n = inject(I18nService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** What the exam's questions may come from. A book's questions are asked for by the book, so they are not hidden behind newer ones from elsewhere. */
  readonly scope = input<ExamScopeDto | undefined>(undefined);

  /** The bank ids of the questions already in the exam. */
  readonly excluded = input<ReadonlySet<string>>(new Set());

  /** The section the question is for, for the buttons' names. */
  readonly sectionName = input('');

  /** True while the page is making a request about the exam or reading it again, so Add cannot be pressed twice. */
  readonly busy = input(false);

  /** The author chose a question: its id in the question bank. */
  readonly picked = output<string>();

  protected readonly difficulties = QUESTION_DIFFICULTIES;

  protected readonly query = signal('');
  protected readonly difficulty = signal<QuestionDifficulty | ''>('');
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly failed = signal(false);
  /** Set when a page after the first could not be read: the list stays as it is and says so, instead of being replaced by an error. */
  protected readonly moreFailed = signal(false);

  /** Every question fetched for the search so far, in the order the API gave them (newest first), before the exam's own limits are applied. */
  private readonly fetched = signal<readonly QuestionDto[]>([]);

  /** Whether the last page was full, so there may be more to ask for. */
  protected readonly hasMore = signal(false);

  /** The questions to list: those fetched that the exam will accept. */
  protected readonly choices = computed(() => this.fetched().filter((question) => !this.excluded().has(question.id) && isInScope(question, this.scope())));

  /** What decides which questions the bank is asked for besides the words typed: the exam's book, or none. */
  private readonly scopeKey = computed(() => {
    const scope = this.scope();
    return scope && scope.type !== 'Independent' ? `${scope.type}:${scope.bookId}` : 'all';
  });

  private readonly typed = new Subject<void>();
  private readonly restart = new Subject<void>();
  private readonly more = new Subject<number>();

  /** Where focus was when an Add was pressed, as the row's place in the list, so it can be put back once the row is gone. */
  private focusAt: number | null = null;

  constructor() {
    // A pause in typing, a change of difficulty, a change of the exam's scope and "Try again" start the list afresh; "Show more" asks for the
    // next page. All of them are one stream: a newer read replaces one still on its way.
    merge(
      this.typed.pipe(
        debounceTime(SEARCH_DELAY_MS),
        map(() => 0),
      ),
      this.restart.pipe(map(() => 0)),
      this.more,
    )
      .pipe(
        switchMap((skip) => this.read(skip)),
        takeUntilDestroyed(),
      )
      .subscribe();

    // The first read, and a read again when the exam's scope changes while the picker is open: the questions it may take are then another
    // book's, which only the bank can say. The effect first runs once the inputs are set.
    effect(() => {
      this.scopeKey();
      untracked(() => this.restart.next());
    });

    // After an Add the row is taken away (once the exam has been read again), which would leave focus on the page's top. It goes to the row that
    // took its place, or to the search box when the list is empty.
    effect(() => {
      this.choices();
      if (!this.busy()) {
        untracked(() => this.restoreFocus());
      }
    });
  }

  protected onSearch(text: string): void {
    this.query.set(text);
    this.typed.next();
  }

  protected onDifficulty(value: string): void {
    this.difficulty.set(value as QuestionDifficulty | '');
    this.restart.next();
  }

  protected retry(): void {
    this.restart.next();
  }

  protected showMore(): void {
    if (this.loadingMore() || this.loading()) {
      return;
    }
    this.loadingMore.set(true);
    this.moreFailed.set(false);
    this.more.next(this.fetched().length);
  }

  protected add(question: QuestionDto): void {
    const rows = Array.from(this.host.nativeElement.querySelectorAll('.picker__row'));
    const at = rows.findIndex((row) => row.contains(document.activeElement));
    this.focusAt = at >= 0 ? at : null;
    this.picked.emit(question.id);
  }

  protected countText(): string {
    return this.i18n.plural('exams.picker.count', this.choices().length);
  }

  protected stem(question: QuestionDto): string {
    return htmlToPlainText(question.text, 160);
  }

  /** What else is worth knowing about a question when choosing: how hard, what topics, where it is filed. */
  protected meta(question: QuestionDto): string {
    const entry = QUESTION_DIFFICULTIES.find((item) => item.value === question.difficulty);
    const level = entry ? this.i18n.t(entry.key) : undefined;
    return [level, question.topics.slice(0, 3).join(', '), question.chapterTitle].filter((part): part is string => !!part).join(' · ');
  }

  /** What the button of a question is called: which question, and which section it would go into. */
  protected addLabel(question: QuestionDto): string {
    return this.i18n.t('exams.picker.addNamed', { question: htmlToPlainText(question.text, 60), section: this.sectionName() });
  }

  /** What the list says when it is empty: that nothing matches a search, or that the exam has nothing left to take. */
  protected emptyText(): string {
    if (this.query().trim() !== '' || this.difficulty() !== '') {
      return this.i18n.t('exams.picker.noMatch');
    }
    switch (this.scope()?.type) {
      case 'Book':
        return this.i18n.t('exams.section.noneLeftBook');
      case 'Chapters':
        return this.i18n.t('exams.section.noneLeftChapters');
      default:
        return this.i18n.t('exams.picker.none');
    }
  }

  protected scopeHint(): string | null {
    switch (this.scope()?.type) {
      case 'Book':
        return this.i18n.t('exams.section.onlyBook');
      case 'Chapters':
        return this.i18n.t('exams.section.onlyChapters');
      default:
        return null;
    }
  }

  /** Puts focus back after an Add took the focused row away, once the page is done with the exam. */
  private restoreFocus(): void {
    const at = this.focusAt;
    if (at === null) {
      return;
    }
    this.focusAt = null;
    afterNextRender(
      () => {
        const root = this.host.nativeElement;
        if (root.contains(document.activeElement) && document.activeElement !== root) {
          return;
        }
        const buttons = root.querySelectorAll<HTMLButtonElement>('.picker__row button');
        const next = buttons.length > 0 ? buttons[Math.min(at, buttons.length - 1)] : root.querySelector<HTMLInputElement>('input[type="search"]');
        next?.focus();
      },
      { injector: this.injector },
    );
  }

  /**
   * Reads a page of the bank for the search as it stands: from the start (`skip` 0) it replaces what is listed, after that it adds to it,
   * leaving out a question already listed (a question added to the bank between two pages moves the later ones along by one).
   */
  private read(skip: number): Observable<unknown> {
    const scope = this.scope();
    const bookId = scope && scope.type !== 'Independent' ? (scope.bookId ?? undefined) : undefined;
    if (skip === 0) {
      this.loading.set(true);
      this.failed.set(false);
      this.moreFailed.set(false);
      this.loadingMore.set(false);
    }

    return this.questionApi.list({ search: this.query().trim() || undefined, difficulty: this.difficulty() || undefined, bookId }, skip, PAGE_SIZE).pipe(
      tap((page) => {
        if (skip === 0) {
          this.fetched.set(page);
        } else {
          const known = new Set(this.fetched().map((question) => question.id));
          this.fetched.set([...this.fetched(), ...page.filter((question) => !known.has(question.id))]);
        }
        this.hasMore.set(page.length >= PAGE_SIZE);
        this.loading.set(false);
        this.loadingMore.set(false);
      }),
      // Only the first page's failure replaces the list; a later page's leaves what is listed and says that more could not be read.
      catchError(() => {
        this.loading.set(false);
        this.loadingMore.set(false);
        if (skip === 0) {
          this.failed.set(true);
        } else {
          this.moreFailed.set(true);
        }
        return EMPTY;
      }),
    );
  }
}
