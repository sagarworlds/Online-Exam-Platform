import { DatePipe } from '@angular/common';
import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable, Subscription } from 'rxjs';
import { BookApiService } from '../../book-management/book-api.service';
import { BookDto } from '../../book-management/book.models';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { QuestionApiService } from '../../question-bank/question-api.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm.service';
import { Disclosure, DisclosureGroup } from '../../shared/disclosure/disclosure';
import { extractErrorMessage } from '../../shared/problem-details';
import { ToastService } from '../../shared/toast/toast.service';
import { RefreshButton } from '../../shared/refresh-button/refresh-button';
import { InstructionTemplateApiService } from '../../instruction-templates/instruction-template-api.service';
import { InstructionTemplateDto } from '../../instruction-templates/instruction-template.models';
import { ExamInstructions } from '../exam-instructions/exam-instructions';
import { ExamApiService } from '../exam-api.service';
import { ContentProtectionRequest, DrawQuestionsRequest, ExamDto, ExamStatus, FocusViolationLimitRequest, MarkingSchemeDto, ShuffleRequest, UpdateExamDetailsRequest } from '../exam.models';
import { ExamMarkingScheme } from '../exam-marking-scheme/exam-marking-scheme';
import { ExamContentProtection } from '../exam-content-protection/exam-content-protection';
import { ExamFocusViolationLimit } from '../exam-focus-violation-limit/exam-focus-violation-limit';
import { ExamPassingPercentage } from '../exam-passing-percentage/exam-passing-percentage';
import { ExamProctoringProfile } from '../exam-proctoring-profile/exam-proctoring-profile';
import { ExamShuffle } from '../exam-shuffle/exam-shuffle';
import { ExamAttemptLimit } from '../exam-attempt-limit/exam-attempt-limit';
import { ExamDetailsForm } from '../exam-details-form/exam-details-form';
import { NO_SCOPE, ScopeSelection, describeScope, isScopeComplete, selectionOf, toScopeRequest } from '../exam-scope-fields/exam-scope';
import { ExamScopeFields } from '../exam-scope-fields/exam-scope-fields';
import { INSTANT_RELEASE, ReleaseSelection, isReleaseComplete, selectionOfRelease, toReleaseRequest } from '../exam-release-fields/exam-release';
import { ExamReleaseFields } from '../exam-release-fields/exam-release-fields';
import { ExamSectionCard } from '../exam-section-card/exam-section-card';
import { ExamSettingsSummary } from './exam-settings-summary';

/** The word each exam status is shown as; the API's own word is kept for the checks that compare it. */
const STATUS_KEYS: Readonly<Record<ExamStatus, MessageKey>> = {
  Draft: 'exams.status.Draft',
  Published: 'exams.status.Published',
  Archived: 'exams.status.Archived',
};

/**
 * Admin page for one exam: its sections and questions, the schedule, and publishing (FR-11, FR-13). While the exam is a
 * draft, mistakes can be put right: a question or section taken out, a section renamed, the whole draft deleted. A published
 * exam is shown read-only, because the API refuses those; only its name and description, its answer review and its attempts
 * allowed can still change.
 */
/** A moment as the page shows it everywhere on this screen: date and time, in the language on screen. */
function formatDateTime(utc: string | null | undefined): string {
  return utc ? new Date(utc).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '';
}

@Component({
  selector: 'app-exam-editor',
  imports: [ReactiveFormsModule, RouterLink, DatePipe, TranslatePipe, ExamScopeFields, ExamReleaseFields, ExamDetailsForm, ExamMarkingScheme, ExamShuffle, ExamAttemptLimit, ExamContentProtection, ExamFocusViolationLimit, ExamPassingPercentage, ExamProctoringProfile, ExamSectionCard, ExamInstructions, RefreshButton, Disclosure, DisclosureGroup],
  templateUrl: './exam-editor.html',
})
export class ExamEditor {
  private readonly formBuilder = inject(FormBuilder);
  private readonly examApi = inject(ExamApiService);
  private readonly questionApi = inject(QuestionApiService);
  private readonly bookApi = inject(BookApiService);
  private readonly templateApi = inject(InstructionTemplateApiService);
  private readonly router = inject(Router);
  private readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  private readonly i18n = inject(I18nService);
  /** One value line per setting, in words, for the rows the page shows closed. */
  protected readonly summary = new ExamSettingsSummary(this.i18n);
  /** The sections heading, which the readiness list moves focus to so the author can add questions. */
  private readonly sectionsTitle = viewChild<ElementRef<HTMLElement>>('sectionsTitle');
  /** The "Delete draft" button that opens the confirmation, and the Cancel button inside it: focus moves between them as it opens and closes. */
  private readonly deleteOpener = viewChild<ElementRef<HTMLButtonElement>>('deleteOpener');
  private readonly deleteCancel = viewChild<ElementRef<HTMLButtonElement>>('deleteCancel');
  private readonly injector = inject(Injector);

  protected readonly exam = signal<ExamDto | null>(null);
  /** Topics in use in the bank, for the sections' draw pickers. */
  protected readonly topics = signal<string[]>([]);
  protected readonly loading = signal(true);
  /** Whether the exam is being read again, after the refresh icon was pressed or a change was stored; the page stays as it is meanwhile. */
  protected readonly refreshing = signal(false);
  /** The read of the exam in flight, so a newer read replaces it and an older answer cannot overwrite what a change has just stored. */
  private reading: Subscription | null = null;
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  /** When the exam opens and closes, for the schedule row; empty while it has no schedule, which the row's tag says instead. */
  protected scheduleSummary(exam: ExamDto): string {
    return exam.isScheduled
      ? this.i18n.t('exams.editor.opensAt', { start: formatDateTime(exam.scheduledStartTime) })
      : '';
  }

  /** The passing mark, in words: a percentage, or that there is none. */
  protected passingSummary(exam: ExamDto): string {
    const percent = exam.config.passingPercentage;
    return percent ? this.i18n.t('exams.passingMark.readSet', { percent }) : this.i18n.t('exams.passingMark.readNone');
  }

  /** What the exam's questions come from, in words, in the language on screen. */
  protected scopeSummary(): string {
    return describeScope(this.exam()?.scope, this.i18n.t);
  }

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
   * The bank ids of the questions already in the exam (a question is included once), which each section's question picker leaves out. The
   * picker reads the bank itself, so the page no longer loads two hundred questions it may never show.
   */
  protected readonly usedQuestionIds = computed<ReadonlySet<string>>(
    () => new Set(this.exam()?.sections?.flatMap((section) => section.questions.map((question) => question.questionId)) ?? []),
  );

  protected readonly sectionForm = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
  });

  constructor() {
    this.reload();
    this.loadTopics();
  }

  /**
   * Reads the exam again, with the questions and topics it can draw on, so a change made elsewhere shows without reloading the tab. The page
   * is not blanked, and an edit that is open stays open until the exam it was started on has changed. A failed read leaves what is shown and
   * says why.
   */
  protected onRefresh(): void {
    if (this.refreshing() || this.busy()) {
      return;
    }

    this.errorMessage.set(null);
    this.reload();
    this.loadTopics();
  }

  // Only feeds a picker; the draw works without it, so a failure is shown but does not stop the page.
  private loadTopics(): void {
    this.questionApi.topics().subscribe({
      next: (topics) => this.topics.set(topics),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }

  /** The status word the header shows, in the language on screen. */
  protected statusLabel(status: ExamStatus): string {
    return this.i18n.t(STATUS_KEYS[status]);
  }

  /** How many questions the exam has, in words: none yet, or the count. */
  protected questionLine(): string {
    const count = this.questionCount();
    return count === 0 ? this.i18n.t('exams.editor.noQuestions') : this.i18n.plural('exams.editor.questionsCount', count);
  }

  /** What still stops the exam being published, in one sentence. The two parts are the ones the publish rule checks. */
  protected publishHint(): string {
    const schedule: MessageKey = this.exam()?.isScheduled ? 'exams.editor.hint.scheduled' : 'exams.editor.hint.schedule';
    const questions: MessageKey = this.questionCount() > 0 ? 'exams.editor.hint.questions' : 'exams.editor.hint.addQuestion';
    return this.i18n.t('exams.editor.publishHint', { schedule: this.i18n.t(schedule), questions: this.i18n.t(questions) });
  }

  /** Moves focus to the sections from the readiness list, so the author lands where the questions are added. */
  protected showSections(): void {
    this.sectionsTitle()?.nativeElement.focus();
  }

  /** The question asked before a draft is deleted, naming it so the author sees which draft it is. */
  protected deleteQuestion(): string {
    return this.i18n.t('exams.editor.deleteQuestion', { name: this.exam()?.name ?? '' });
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

  /** The instruction templates the author can copy from. Loaded the first time the Instructions setting is opened (FR-41). */
  protected readonly templates = signal<InstructionTemplateDto[]>([]);
  protected readonly templatesLoading = signal(false);
  protected readonly templatesError = signal<string | null>(null);
  private templatesLoaded = false;

  /** Loads the templates the first time the Instructions setting is opened, so the page does not ask for them before they are needed. */
  protected onInstructionsOpenChange(open: boolean): void {
    if (open && !this.templatesLoaded) {
      this.loadTemplates();
    }
  }

  /** Saves the instructions a candidate reads. Draft exams only: a candidate acknowledges them before each attempt. */
  protected saveInstructions(text: string): void {
    if (!this.busy() && this.isDraft()) {
      this.run(this.examApi.setInstructions(this.examId, text));
    }
  }

  /** Copies an instruction template's text into the exam's instructions. Draft exams only, for the same reason. */
  protected useInstructionTemplate(templateId: string): void {
    if (!this.busy() && this.isDraft()) {
      this.run(this.examApi.useInstructionTemplate(this.examId, templateId));
    }
  }

  private loadTemplates(): void {
    this.templatesLoaded = true;
    this.templatesLoading.set(true);
    this.templatesError.set(null);
    this.templateApi.list().subscribe({
      next: (templates) => {
        this.templates.set(templates);
        this.templatesLoading.set(false);
      },
      error: (error: unknown) => {
        // Not marked as loaded, so opening the setting again asks once more.
        this.templatesLoaded = false;
        this.templatesLoading.set(false);
        this.templatesError.set(extractErrorMessage(error, this.i18n.t('exams.instructions.loadFailed')));
      },
    });
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

  /** Applies a proctoring profile, which sets the copy block and the page-leaving limit together. Allowed on a published exam too. */
  protected saveProctoringProfile(profile: string): void {
    if (!this.busy()) {
      this.run(this.examApi.setProctoringProfile(this.examId, { profile }));
    }
  }

  /** Sets how many times a candidate may leave the exam page before the attempt is ended. Allowed on a published exam too. */
  protected saveFocusViolationLimit(request: FocusViolationLimitRequest): void {
    if (!this.busy()) {
      this.run(this.examApi.setFocusViolationLimit(this.examId, request));
    }
  }

  /** Sets the passing mark, or removes it when the author turned it off (FR-34). */
  protected savePassingPercentage(percent: number | null): void {
    if (this.busy()) {
      return;
    }
    this.run(
      percent === null
        ? this.examApi.clearPassingPercentage(this.examId)
        : this.examApi.setPassingPercentage(this.examId, { passingPercentage: percent }),
    );
  }

  protected releaseNow(): void {
    if (!this.canReleaseNow() || this.busy()) {
      return;
    }

    this.run(this.examApi.releaseResults(this.examId), undefined, 'exams.editor.released');
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

  /** Opens the confirmation with focus on Cancel, so a stray Enter does not delete the draft. */
  protected openDeleteConfirmation(): void {
    this.confirmingDelete.set(true);
    afterNextRender(() => this.deleteCancel()?.nativeElement.focus(), { injector: this.injector });
  }

  /** Closes the confirmation without deleting, and gives focus back to the button that opened it. */
  protected closeDeleteConfirmation(): void {
    this.confirmingDelete.set(false);
    afterNextRender(() => this.deleteOpener()?.nativeElement.focus(), { injector: this.injector });
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
        // The confirmation's button was removed when the delete began, so focus is put back on the button that opens it.
        afterNextRender(() => this.deleteOpener()?.nativeElement.focus(), { injector: this.injector });
      },
    });
  }

  /**
   * Publishes the exam, after saying what that fixes: once it is published its questions, scope, marking and instructions cannot be edited
   * and candidates can start it, so this is the one button on the page that is asked about.
   */
  protected async publish(): Promise<void> {
    if (!this.canPublish() || this.busy()) {
      return;
    }

    const opens = this.exam()?.scheduledStartTime;
    const confirmed = await this.confirm.ask({
      title: this.i18n.t('exams.editor.publishTitle'),
      message: opens ? this.i18n.t('exams.editor.publishOpens', { start: formatDateTime(opens) }) : undefined,
      consequences: [this.i18n.t('exams.editor.publishFixes.questions'), this.i18n.t('exams.editor.publishFixes.settings')],
      confirmLabel: this.i18n.t('exams.editor.publish'),
    });
    if (confirmed && !this.busy()) {
      this.run(this.examApi.publish(this.examId), undefined, 'exams.editor.published');
    }
  }

  // Runs one change, then reloads the exam so the page always shows what the server stored, and says that it was stored.
  private run(call: Observable<unknown>, afterSuccess?: () => void, saved: MessageKey = 'exams.editor.saved'): void {
    this.busy.set(true);
    this.errorMessage.set(null);
    call.subscribe({
      next: () => {
        afterSuccess?.();
        this.toast.success(this.i18n.t(saved));
        this.busy.set(false);
        this.reload();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  private reload(): void {
    this.refreshing.set(true);
    this.reading?.unsubscribe();
    this.reading = this.examApi.getExamById(this.examId).subscribe({
      next: (exam) => {
        this.exam.set(exam);
        this.loading.set(false);
        this.refreshing.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.refreshing.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
