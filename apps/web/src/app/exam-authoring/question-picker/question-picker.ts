import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Subject, catchError, debounceTime, merge, switchMap, tap } from 'rxjs';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { QuestionApiService } from '../../question-bank/question-api.service';
import { QUESTION_DIFFICULTIES, QuestionDifficulty, QuestionDto } from '../../question-bank/question.models';
import { FieldControl, FormField } from '../../shared/form-field/form-field';
import { htmlToPlainText } from '../../shared/rich-text/html-to-text';
import { PlainTextPipe } from '../../shared/rich-text/plain-text.pipe';
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
 * `excluded` then drops the question from the list.
 */
@Component({
  selector: 'app-question-picker',
  imports: [FormField, FieldControl, PlainTextPipe, TranslatePipe],
  templateUrl: './question-picker.html',
  styleUrl: './question-picker.css',
})
export class QuestionPicker implements OnInit {
  private readonly questionApi = inject(QuestionApiService);
  private readonly i18n = inject(I18nService);

  /** What the exam's questions may come from. A book's questions are asked for by the book, so they are not hidden behind newer ones from elsewhere. */
  readonly scope = input<ExamScopeDto | undefined>(undefined);

  /** The bank ids of the questions already in the exam. */
  readonly excluded = input<ReadonlySet<string>>(new Set());

  /** The section the question is for, for the buttons' names. */
  readonly sectionName = input('');

  /** True while the page is making a request about the exam, so Add cannot be pressed twice. */
  readonly busy = input(false);

  /** The author chose a question: its id in the question bank. */
  readonly picked = output<string>();

  protected readonly difficulties = QUESTION_DIFFICULTIES;

  protected readonly query = signal('');
  protected readonly difficulty = signal<QuestionDifficulty | ''>('');
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly failed = signal(false);

  /** Every question fetched for the search so far, in the order the API gave them (newest first), before the exam's own limits are applied. */
  private readonly fetched = signal<readonly QuestionDto[]>([]);

  /** Whether the last page was full, so there may be more to ask for. */
  protected readonly hasMore = signal(false);

  /** The questions to list: those fetched that the exam will accept. */
  protected readonly choices = computed(() => this.fetched().filter((question) => !this.excluded().has(question.id) && isInScope(question, this.scope())));

  private readonly typed = new Subject<void>();
  private readonly restart = new Subject<void>();

  constructor() {
    // A pause in typing, a change of difficulty and "Try again" all start the list afresh; a newer read replaces one still on its way, so a slow
    // answer to an old search can never overwrite the answer to the current one.
    merge(this.typed.pipe(debounceTime(SEARCH_DELAY_MS)), this.restart)
      .pipe(
        switchMap(() => this.read(0)),
        takeUntilDestroyed(),
      )
      .subscribe();
  }

  ngOnInit(): void {
    // The first read waits for the inputs, since a scoped exam asks for its own book's questions.
    this.restart.next();
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
    this.read(this.fetched().length).subscribe();
  }

  /** How many questions are listed, for a screen reader to hear after a search. */
  protected countText(): string {
    return this.i18n.plural('exams.picker.count', this.choices().length);
  }

  protected stem(question: QuestionDto): string {
    return htmlToPlainText(question.text, 160);
  }

  /** What else is worth knowing about a question when choosing: how hard, what topics, where it is filed. */
  protected meta(question: QuestionDto): string {
    const level = QUESTION_DIFFICULTIES.find((entry) => entry.value === question.difficulty)?.label;
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

  /** Reads a page of the bank for the search as it stands: from the start (`skip` 0) it replaces what is listed, after that it adds to it. */
  private read(skip: number) {
    const scope = this.scope();
    const bookId = scope && scope.type !== 'Independent' ? (scope.bookId ?? undefined) : undefined;
    if (skip === 0) {
      this.loading.set(true);
      this.failed.set(false);
    }

    return this.questionApi
      .list({ search: this.query().trim() || undefined, difficulty: this.difficulty() || undefined, bookId }, skip, PAGE_SIZE)
      .pipe(
        tap((page) => {
          this.fetched.set(skip === 0 ? page : [...this.fetched(), ...page]);
          this.hasMore.set(page.length >= PAGE_SIZE);
          this.loading.set(false);
          this.loadingMore.set(false);
        }),
        catchError(() => {
          this.loading.set(false);
          this.loadingMore.set(false);
          this.failed.set(true);
          return EMPTY;
        }),
      );
  }
}
