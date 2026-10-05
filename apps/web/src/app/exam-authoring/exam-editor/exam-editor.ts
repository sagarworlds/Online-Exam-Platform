import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { BookApiService } from '../../book-management/book-api.service';
import { BookDto } from '../../book-management/book.models';
import { QuestionApiService } from '../../question-bank/question-api.service';
import { QuestionDto } from '../../question-bank/question.models';
import { extractErrorMessage } from '../../shared/problem-details';
import { ExamApiService } from '../exam-api.service';
import { ContentProtectionRequest, DrawQuestionsRequest, ExamDto, ExamScopeDto, MarkingSchemeDto, ShuffleRequest, UpdateExamDetailsRequest } from '../exam.models';
import { ExamMarkingScheme } from '../exam-marking-scheme/exam-marking-scheme';
import { ExamContentProtection } from '../exam-content-protection/exam-content-protection';
import { ExamShuffle } from '../exam-shuffle/exam-shuffle';
import { ExamAttemptLimit } from '../exam-attempt-limit/exam-attempt-limit';
import { ExamDetailsForm } from '../exam-details-form/exam-details-form';
import { NO_SCOPE, ScopeSelection, describeScope, isScopeComplete, selectionOf, toScopeRequest } from '../exam-scope-fields/exam-scope';
import { ExamScopeFields } from '../exam-scope-fields/exam-scope-fields';
import { INSTANT_RELEASE, ReleaseSelection, isReleaseComplete, selectionOfRelease, toReleaseRequest } from '../exam-release-fields/exam-release';
import { ExamReleaseFields } from '../exam-release-fields/exam-release-fields';
import { ExamSectionCard } from '../exam-section-card/exam-section-card';

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
 * Admin page for one exam: its sections and questions, the schedule, and publishing (FR-11, FR-13). While the exam is a
 * draft, mistakes can be put right: a question or section taken out, a section renamed, the whole draft deleted. A published
 * exam is shown read-only, because the API refuses those; only its name and description, its answer review and its attempts
 * allowed can still change.
 */
@Component({
  selector: 'app-exam-editor',
  imports: [ReactiveFormsModule, RouterLink, DatePipe, ExamScopeFields, ExamReleaseFields, ExamDetailsForm, ExamMarkingScheme, ExamShuffle, ExamAttemptLimit, ExamContentProtection, ExamSectionCard],
  templateUrl: './exam-editor.html',
})
export class ExamEditor {
  private readonly formBuilder = inject(FormBuilder);
  private readonly examApi = inject(ExamApiService);
  private readonly questionApi = inject(QuestionApiService);
  private readonly bookApi = inject(BookApiService);
  private readonly router = inject(Router);
  private readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly exam = signal<ExamDto | null>(null);
  protected readonly bank = signal<QuestionDto[]>([]);
  /** Topics in use in the bank, for the sections' draw pickers. */
  protected readonly topics = signal<string[]>([]);
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

  /** Changing the name and description. Allowed on a published exam too: it changes nothing that is asked or scored. */
  protected readonly changingDetails = signal(false);
  protected readonly canChangeDetails = computed(() => this.exam() !== null && this.exam()?.status !== 'Archived');

  /** Asking "delete this draft for good?" inline, instead of in a dialog. */
  protected readonly confirmingDelete = signal(false);

  protected readonly isDraft = computed(() => this.exam()?.status === 'Draft');
  protected readonly canChangeRelease = computed(() => this.exam() !== null && this.exam()?.status !== 'Archived');
  /** A manual-release exam that is published and whose answers have not been released yet. */
  protected readonly canReleaseNow = computed(() => {
    const exam = this.exam();
    return exam?.status === 'Published' && exam.config.resultReleaseMode === 'Manual' && exam.config.resultReleaseTime === null;
  });
  protected readonly questionCount = computed(
    // A rule counts for what it draws: an exam made only of rules still has questions for every candidate.
    () =>
      this.exam()?.sections?.reduce(
        (total, section) => total + section.questions.length + (section.drawRules ?? []).reduce((sum, rule) => sum + rule.count, 0),
        0,
      ) ?? 0,
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
    // Only feeds a picker; the draw works without it, so a failure is shown but does not stop the page.
    this.questionApi.topics().subscribe({
      next: (topics) => this.topics.set(topics),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
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

  /** Chooses whether questions and options are shuffled. Draft exams only, so the order of every attempt stays the same on reload and review. */
  protected saveShuffle(request: ShuffleRequest): void {
    if (!this.busy() && this.isDraft()) {
      this.run(this.examApi.setShuffle(this.examId, request));
    }
  }

  /** Sets the marks per answer. Draft exams only, so every attempt at a published exam is scored the same way. */
  protected saveMarkingScheme(scheme: MarkingSchemeDto): void {
    if (!this.busy() && this.isDraft()) {
      this.run(this.examApi.setMarkingScheme(this.examId, scheme));
    }
  }

  /** Sets how many attempts every candidate has. Allowed on a published exam too: it changes nothing that is asked or scored. */
  protected saveAttemptLimit(maxAttempts: number): void {
    if (!this.busy()) {
      this.run(this.examApi.setAttemptLimit(this.examId, { maxAttempts }));
    }
  }

  /** Turns the exam page's copy, paste, right-click and print protection on or off. Allowed on a published exam too. */
  protected saveContentProtection(request: ContentProtectionRequest): void {
    if (!this.busy()) {
      this.run(this.examApi.setContentProtection(this.examId, request));
    }
  }

  protected releaseNow(): void {
    if (!this.canReleaseNow() || this.busy()) {
      return;
    }

    this.run(this.examApi.releaseResults(this.examId));
  }

  protected startChangingDetails(): void {
    this.changingDetails.set(true);
  }

  protected cancelChangingDetails(): void {
    this.changingDetails.set(false);
  }

  protected saveDetails(request: UpdateExamDetailsRequest): void {
    if (this.busy()) {
      return;
    }

    this.run(this.examApi.updateDetails(this.examId, request), () => this.changingDetails.set(false));
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

  protected addDrawRule(sectionId: string, request: DrawQuestionsRequest): void {
    if (!this.busy()) {
      this.run(this.examApi.addDrawRule(this.examId, sectionId, request));
    }
  }

  protected removeDrawRule(target: { sectionId: string; ruleId: string }): void {
    if (!this.busy()) {
      this.run(this.examApi.removeDrawRule(this.examId, target.sectionId, target.ruleId));
    }
  }

  protected drawQuestions(sectionId: string, request: DrawQuestionsRequest): void {
    if (!this.busy()) {
      this.run(this.examApi.drawQuestions(this.examId, sectionId, request));
    }
  }

  protected removeQuestion(target: { sectionId: string; questionId: string }): void {
    if (!this.busy()) {
      this.run(this.examApi.removeQuestion(this.examId, target.sectionId, target.questionId));
    }
  }

  protected removeSection(sectionId: string): void {
    if (!this.busy()) {
      this.run(this.examApi.removeSection(this.examId, sectionId));
    }
  }

  protected renameSection(change: { sectionId: string; name: string }): void {
    if (this.busy()) {
      return;
    }

    // The API replaces the name and the time limit together, so the limit the section already has is sent back as it is;
    // renaming must not quietly remove it.
    const timeSeconds = this.exam()?.sections?.find((section) => section.id === change.sectionId)?.timeSeconds ?? null;
    this.run(this.examApi.editSection(this.examId, change.sectionId, { name: change.name, timeSeconds }));
  }

  protected deleteDraft(): void {
    if (this.busy()) {
      return;
    }

    this.confirmingDelete.set(false);
    this.busy.set(true);
    this.errorMessage.set(null);
    this.examApi.deleteExam(this.examId).subscribe({
      // Nothing to reload: the exam is gone, so go back to the list that no longer has it.
      next: () => void this.router.navigate(['/exams']),
      error: (error: unknown) => {
        this.busy.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
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
