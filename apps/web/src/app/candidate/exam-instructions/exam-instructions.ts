import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { MyExamDto } from '../candidate.models';
import { CheckResult, CheckStatus, SystemCheckService } from '../system-check/system-check';
import { instructionLines, timeAllowed } from './exam-rules-text';

/** What each result is called aloud, so the symbol next to it is never the only thing that carries its meaning. */
const STATUS_WORDS: Record<CheckStatus, string> = {
  pass: 'Passed',
  warn: 'Warning',
  fail: 'Problem',
  info: 'Not measured',
};

/**
 * The page between "Start exam" and the exam itself (FR-17): the exam's instructions, a check that this browser and connection can
 * sit it, and an acknowledgment the candidate must give before an attempt begins. The server enforces the acknowledgment too; this
 * page is how a candidate gives it.
 */
@Component({
  selector: 'app-exam-instructions',
  imports: [DatePipe, RouterLink],
  templateUrl: './exam-instructions.html',
  styleUrl: './exam-instructions.css',
})
export class ExamInstructions {
  private readonly api = inject(CandidateApiService);
  private readonly router = inject(Router);
  private readonly systemCheck = inject(SystemCheckService);
  private readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('examId') ?? '';

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly exam = signal<MyExamDto | null>(null);

  /** The check results, or null while the check is running. */
  protected readonly checks = signal<CheckResult[] | null>(null);
  protected readonly acknowledged = signal(false);
  protected readonly starting = signal(false);
  protected readonly startError = signal<string | null>(null);

  protected readonly statusWords = STATUS_WORDS;
  protected readonly timeAllowed = timeAllowed;

  protected readonly lines = computed(() => {
    const exam = this.exam();
    return exam === null ? [] : instructionLines(exam);
  });

  /** Whether the check found something that stops the exam from being sat. Warnings never do. */
  protected readonly hasProblem = computed(() => this.checks()?.some((c) => c.status === 'fail') ?? false);

  /** Whether any check came out as a warning, so the page can say it is safe to go on anyway. */
  protected readonly hasWarning = computed(() => this.checks()?.some((c) => c.status === 'warn') ?? false);

  protected readonly canStart = computed(
    () => this.exam()?.canStartAttempt === true && this.checks() !== null && !this.hasProblem() && this.acknowledged() && !this.starting(),
  );

  /** Why a new attempt cannot be started from here, or null when one can. */
  protected readonly cannotStartReason = computed(() => {
    const exam = this.exam();
    if (exam === null || exam.canStartAttempt) {
      return null;
    }

    if (exam.state === 'NotOpen') {
      return 'This exam has not opened yet.';
    }

    if (exam.state === 'Closed') {
      return 'This exam is closed, so a new attempt can no longer be started.';
    }

    return exam.attemptStatus === 'InProgress'
      ? 'You already have an attempt in progress.'
      : 'You have used every attempt you have at this exam.';
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
    this.api.startAttempt(this.examId, true).subscribe({
      next: (attempt) => void this.router.navigate(['/attempt', attempt.id]),
      error: (error: unknown) => {
        this.starting.set(false);
        this.startError.set(extractErrorMessage(error));
      },
    });
  }
}
