import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { BookApiService } from '../../book-management/book-api.service';
import { BookDto } from '../../book-management/book.models';
import { QuestionApiService } from '../../question-bank/question-api.service';
import { QuestionDto } from '../../question-bank/question.models';
import { PlainTextPipe } from '../../shared/rich-text/plain-text.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { ExamApiService } from '../exam-api.service';
import { ExamDto, ExamScopeDto } from '../exam.models';
import { NO_SCOPE, ScopeSelection, describeScope, isScopeComplete, selectionOf, toScopeRequest } from '../exam-scope-fields/exam-scope';
import { ExamScopeFields } from '../exam-scope-fields/exam-scope-fields';
import { INSTANT_RELEASE, ReleaseSelection, isReleaseComplete, selectionOfRelease, toReleaseRequest } from '../exam-release-fields/exam-release';
import { ExamReleaseFields } from '../exam-release-fields/exam-release-fields';

/** Whether a question may go into an exam with this scope; mirrors the rule the API enforces. */
function isInScope(question: QuestionDto, scope: ExamScopeDto | undefined): boolean {
  switch (scope?.type) {
    case 'Book':
      return question.bookId !== null && question.bookId === scope.bookId;
    case 'Chapters':
      return question.chapterId !== null && scope.chapters.some((chapter) => chapter.id === question.chapterId);
    default:
      return true;
  }
}

/**
 * Admin page for one exam: its sections and questions, the schedule, and publishing (FR-11, FR-13).
 * A published exam is shown read-only, because the API refuses edits to it.
 */
@Component({
  selector: 'app-exam-editor',
  imports: [ReactiveFormsModule, RouterLink, DatePipe, PlainTextPipe, ExamScopeFields, ExamReleaseFields],
  templateUrl: './exam-editor.html',
})
export class ExamEditor {
  private readonly formBuilder = inject(FormBuilder);
  private readonly examApi = inject(ExamApiService);
  private readonly questionApi = inject(QuestionApiService);
  private readonly bookApi = inject(BookApiService);
  private readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly exam = signal<ExamDto | null>(null);
  protected readonly bank = signal<QuestionDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /** What the exam's questions come from, in words. */
  protected readonly scopeSummary = computed(() => describeScope(this.exam()?.scope));
  protected readonly isScoped = computed(() => (this.exam()?.scope.type ?? 'Independent') !== 'Independent');

  /** Changing the scope: the books to choose from are read only when the author asks to change it. */
  protected readonly changingScope = signal(false);
  protected readonly books = signal<BookDto[]>([]);
  protected readonly booksState = signal<'idle' | 'loading' | 'ready' | 'unavailable'>('idle');
  protected readonly scopeDraft = signal<ScopeSelection>(NO_SCOPE);
  protected readonly scopeDraftComplete = computed(() => isScopeComplete(this.scopeDraft()));

  /** Changing when candidates may see which answers were right. Allowed on a published exam too: the review is worked out when asked for. */
  protected readonly changingRelease = signal(false);
  protected readonly releaseDraft = signal<ReleaseSelection>(INSTANT_RELEASE);
  protected readonly releaseDraftComplete = computed(() => isReleaseComplete(this.releaseDraft()));

  protected readonly isDraft = computed(() => this.exam()?.status === 'Draft');
  protected readonly canChangeRelease = computed(() => this.exam() !== null && this.exam()?.status !== 'Archived');
  /** A manual-release exam that is published and whose answers have not been released yet. */
  protected readonly canReleaseNow = computed(() => {
    const exam = this.exam();
    return exam?.status === 'Published' && exam.config.resultReleaseMode === 'Manual' && exam.config.resultReleaseTime === null;
  });
  protected readonly questionCount = computed(
    () => this.exam()?.sections?.reduce((total, section) => total + section.questions.length, 0) ?? 0,
  );
  protected readonly canPublish = computed(
    () => this.isDraft() && this.exam()?.isScheduled === true && this.questionCount() > 0,
  );

  /**
   * Bank questions that may go into this exam: not already in it (a question is included once), and inside its scope.
   * The API refuses the rest anyway; offering only what it will accept saves the author finding that out one by one.
   */
  protected readonly availableQuestions = computed(() => {
    const used = new Set(
      this.exam()?.sections?.flatMap((section) => section.questions.map((question) => question.questionId)) ?? [],
    );
    const scope = this.exam()?.scope;
    return this.bank().filter((question) => !used.has(question.id) && isInScope(question, scope));
  });

  /** The scope the bank list was loaded for, so it is read again only when the scope changes. */
  private bankKey: string | null = null;

  protected readonly sectionForm = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
  });

  constructor() {
    this.reload();
  }

  protected startChangingScope(): void {
    this.scopeDraft.set(selectionOf(this.exam()?.scope));
    this.changingScope.set(true);

    if (this.booksState() === 'idle') {
      this.booksState.set('loading');
      this.bookApi.list().subscribe({
        next: (books) => {
          this.books.set(books);
          this.booksState.set('ready');
        },
        error: (error: unknown) => {
          console.error('Books could not be loaded for the scope choice', error);
          this.booksState.set('unavailable');
        },
      });
    }
  }

  protected cancelChangingScope(): void {
    this.changingScope.set(false);
  }

  protected saveScope(): void {
    if (!this.scopeDraftComplete() || this.busy()) {
      return;
    }

    this.run(this.examApi.setScope(this.examId, toScopeRequest(this.scopeDraft())), () => this.changingScope.set(false));
  }

  protected startChangingRelease(): void {
    this.releaseDraft.set(selectionOfRelease(this.exam()?.config));
    this.changingRelease.set(true);
  }

  protected cancelChangingRelease(): void {
    this.changingRelease.set(false);
  }

  protected saveRelease(): void {
    if (!this.releaseDraftComplete() || this.busy()) {
      return;
    }

    this.run(this.examApi.setResultRelease(this.examId, toReleaseRequest(this.releaseDraft())), () => this.changingRelease.set(false));
  }

  protected releaseNow(): void {
    if (!this.canReleaseNow() || this.busy()) {
      return;
    }

    this.run(this.examApi.releaseResults(this.examId));
  }

  protected addSection(): void {
    if (this.sectionForm.invalid || this.busy()) {
      return;
    }

    this.run(this.examApi.addSection(this.examId, this.sectionForm.getRawValue().name.trim()), () =>
      this.sectionForm.reset({ name: '' }),
    );
  }

  protected addQuestion(sectionId: string, questionId: string): void {
    if (this.busy()) {
      return;
    }

    // Said out loud rather than ignored: the list behind the picker is redrawn after every change, which can
    // clear a choice made a moment earlier, and a silent no-op would look like a button that does nothing.
    if (!questionId) {
      this.errorMessage.set('Choose a question to add.');
      return;
    }

    this.run(this.examApi.addQuestion(this.examId, sectionId, questionId));
  }

  protected publish(): void {
    if (!this.canPublish() || this.busy()) {
      return;
    }

    this.run(this.examApi.publish(this.examId));
  }

  // Runs one change, then reloads the exam so the page always shows what the server stored.
  private run(call: Observable<unknown>, afterSuccess?: () => void): void {
    this.busy.set(true);
    this.errorMessage.set(null);
    call.subscribe({
      next: () => {
        afterSuccess?.();
        this.busy.set(false);
        this.reload();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  // A scoped exam is offered the questions of its book, read by the API's own filter, so the newest-200 cap of the
  // unfiltered list cannot hide a chapter's questions behind newer ones from elsewhere.
  private loadBank(scope: ExamScopeDto | undefined): void {
    const key = scope && scope.type !== 'Independent' ? `${scope.type}:${scope.bookId}` : 'all';
    if (key === this.bankKey) {
      return;
    }

    this.bankKey = key;
    this.questionApi.list(key === 'all' ? {} : { bookId: scope?.bookId ?? undefined }).subscribe({
      next: (questions) => this.bank.set(questions),
      error: (error: unknown) => {
        // Forget the key, so the next reload tries again instead of leaving the picker empty for good.
        this.bankKey = null;
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  private reload(): void {
    this.examApi.getExamById(this.examId).subscribe({
      next: (exam) => {
        this.exam.set(exam);
        this.loading.set(false);
        this.loadBank(exam.scope);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
