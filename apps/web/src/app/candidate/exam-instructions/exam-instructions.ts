import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { AuthSessionService } from '../../auth/auth-session.service';
import { ConsentApiService } from '../../consent/consent-api.service';
import { extractErrorMessage } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { MyExamDto } from '../candidate.models';
import { CheckResult, CheckStatus, SystemCheckService } from '../system-check/system-check';
import { instructionLines, timeAllowed } from './exam-rules-text';
import { switchMap } from 'rxjs';
import { LocaleDatePipe } from '../../i18n/locale-date.pipe';

/**
 * Where the candidate stands on the consent an exam needs before it starts: being checked, not readable, given already (or no notice to give
 * it to), or still to be given on this page against the notice's current version.
 */
type ConsentState = { state: 'checking' } | { state: 'failed' } | { state: 'done' } | { state: 'needed'; noticeVersionId: string };

/**
 * The page between "Start exam" and the exam itself (FR-17): the exam's instructions, a check that this browser and connection can
 * sit it, and an acknowledgment the candidate must give before an attempt begins. The server enforces the acknowledgment too; this
 * page is how a candidate gives it.
 */
@Component({
  selector: 'app-exam-instructions',
  imports: [LocaleDatePipe, RouterLink, TranslatePipe],
  templateUrl: './exam-instructions.html',
  styleUrl: './exam-instructions.css',
})
export class ExamInstructions {
  private readonly api = inject(CandidateApiService);
  private readonly i18n = inject(I18nService);
  private readonly router = inject(Router);
  private readonly systemCheck = inject(SystemCheckService);
  private readonly consentApi = inject(ConsentApiService);
  private readonly userId = inject(AuthSessionService).session()?.userId ?? null;
  private readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('examId') ?? '';

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly exam = signal<MyExamDto | null>(null);

  /** The check results, or null while the check is running. */
  protected readonly checks = signal<CheckResult[] | null>(null);
  protected readonly acknowledged = signal(false);

  /** The proctoring consent: an exam records focus and device signals, so the candidate agrees to that before the clock starts. */
  protected readonly consent = signal<ConsentState>({ state: 'checking' });
  protected readonly consentTicked = signal(false);
  protected readonly starting = signal(false);
  protected readonly startError = signal<string | null>(null);

  /** What each result is called aloud, so the symbol next to it is never the only thing that carries its meaning. */
  protected statusWord(status: CheckStatus): string {
    return this.i18n.t(`check.${status}`);
  }

  protected timeAllowed(exam: MyExamDto): string {
    return timeAllowed(exam, this.i18n);
  }

  /** The extra time the candidate's accommodation gives, in words (FR-49); null when it gives none. */
  protected extraTime(exam: MyExamDto): string | null {
    const seconds = exam.accommodation?.extraTimeSeconds ?? 0;
    return seconds > 0 ? this.i18n.plural('time.minutes', Math.round(seconds / 60)) : null;
  }

  protected readonly lines = computed(() => {
    const exam = this.exam();
    return exam === null ? [] : instructionLines(exam, this.i18n);
  });

  /** Whether the check found something that stops the exam from being sat. Warnings never do. */
  protected readonly hasProblem = computed(() => this.checks()?.some((c) => c.status === 'fail') ?? false);

  /** Whether any check came out as a warning, so the page can say it is safe to go on anyway. */
  protected readonly hasWarning = computed(() => this.checks()?.some((c) => c.status === 'warn') ?? false);

  /** Whether the consent is settled: already given, nothing to give it to, or ticked here and given when Start is pressed. */
  protected readonly consentSettled = computed(() => {
    const consent = this.consent();
    return consent.state === 'done' || (consent.state === 'needed' && this.consentTicked());
  });

  protected readonly canStart = computed(
    () =>
      this.exam()?.canStartAttempt === true &&
      this.checks() !== null &&
      !this.hasProblem() &&
      this.acknowledged() &&
      this.consentSettled() &&
      !this.starting(),
  );

  /**
   * The one line beside Start that says what is still needed, in the order the candidate would meet it: the check, then any
   * problem, then the box. Empty while the start request is in flight, since the button already says it is starting.
   */
  protected readonly startReason = computed(() => {
    if (this.starting()) {
      return '';
    }

    if (this.checks() === null) {
      return this.i18n.t('instructions.checking');
    }

    if (this.hasProblem()) {
      return this.i18n.t('instructions.bar.problem');
    }

    if (!this.acknowledged()) {
      return this.i18n.t('instructions.bar.tick');
    }

    if (this.consent().state === 'failed') {
      return this.i18n.t('instructions.consent.failed');
    }

    return this.consentSettled() ? this.i18n.t('instructions.bar.ready') : this.i18n.t('instructions.bar.consent');
  });

  /**
   * The state of the question paper for this exam: locked until it opens, open once it has, and nothing once it is closed. A paper is
   * only ever shown to a candidate who has started, so this is what the page tells them before they do (#57).
   */
  protected readonly paperStatus = computed<'locked' | 'open' | null>(() => {
    const state = this.exam()?.state;
    return state === 'NotOpen' ? 'locked' : state === 'Open' ? 'open' : null;
  });

  /** Why a new attempt cannot be started from here, or null when one can. */
  protected readonly cannotStartReason = computed(() => {
    const exam = this.exam();
    if (exam === null || exam.canStartAttempt) {
      return null;
    }

    if (exam.state === 'NotOpen') {
      return this.i18n.t('instructions.cannot.notOpen');
    }

    if (exam.state === 'Closed') {
      return this.i18n.t('instructions.cannot.closed');
    }

    return this.i18n.t(exam.attemptStatus === 'InProgress' ? 'instructions.cannot.inProgress' : 'instructions.cannot.noAttempts');
  });

  constructor() {
    this.api.listMyExams().subscribe({
      next: (exams) => {
        this.exam.set(exams.find((e) => e.examId === this.examId) ?? null);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(extractErrorMessage(error));
      },
    });
    void this.runChecks();
    this.readConsent();
  }

  /** Asks whether the candidate has already agreed to the current notice. Without a session (it cannot be recorded) or without a notice, nothing is asked of them. */
  protected readConsent(): void {
    if (this.userId === null) {
      this.consent.set({ state: 'done' });
      return;
    }

    this.consent.set({ state: 'checking' });
    this.consentApi.getStatus(this.userId, 'ProctoringDataProcessing').subscribe({
      next: (status) => {
        const needed = !status.isActive && status.currentNoticeVersionId !== null;
        this.consent.set(needed ? { state: 'needed', noticeVersionId: status.currentNoticeVersionId! } : { state: 'done' });
      },
      error: () => this.consent.set({ state: 'failed' }),
    });
  }

  protected setConsentTicked(event: Event): void {
    this.consentTicked.set((event.target as HTMLInputElement).checked);
  }

  /** Runs the system check, showing each line as "checking" until it is done. Also bound to "Run the check again". */
  protected async runChecks(): Promise<void> {
    this.checks.set(null);
    this.checks.set(await this.systemCheck.run());
  }

  protected setAcknowledged(event: Event): void {
    this.acknowledged.set((event.target as HTMLInputElement).checked);
  }

  /** Begins the attempt (the clock starts here, on the server) and opens it. */
  protected start(): void {
    if (!this.canStart()) {
      return;
    }

    this.starting.set(true);
    this.startError.set(null);
    // The consent is recorded first, so an attempt is never begun on a consent that could not be kept.
    const consent = this.consent();
    const begin = this.api.startAttempt(this.examId, true);
    const call =
      consent.state === 'needed' && this.userId !== null
        ? this.consentApi.grant({ subjectId: this.userId, purpose: 'ProctoringDataProcessing', noticeVersionId: consent.noticeVersionId }).pipe(switchMap(() => begin))
        : begin;
    call.subscribe({
      next: (attempt) => void this.router.navigate(['/attempt', attempt.id]),
      error: (error: unknown) => {
        this.starting.set(false);
        this.startError.set(extractErrorMessage(error));
      },
    });
  }
}
