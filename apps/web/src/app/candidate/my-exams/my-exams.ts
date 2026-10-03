import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { bestAttemptId } from '../best-attempt';
import { MAX_ATTEMPT_REQUEST_TEXT, MyExamDto } from '../candidate.models';

/** The candidate's page: the exams they have accepted an invitation to, and whether each can be started now (FR-16). */
@Component({
  selector: 'app-my-exams',
  imports: [DatePipe, RouterLink],
  templateUrl: './my-exams.html',
})
export class MyExams {
  private readonly api = inject(CandidateApiService);
  private readonly router = inject(Router);

  protected readonly exams = signal<MyExamDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  /** The exam whose attempt is being created, so its button is disabled against a double click. */
  protected readonly startingExamId = signal<string | null>(null);
  protected readonly bestAttemptId = bestAttemptId;

  /** The exam whose "ask for another attempt" form is open; one at a time. */
  protected readonly requestingFor = signal<string | null>(null);
  protected readonly requestMessage = signal('');
  protected readonly requestBusy = signal(false);
  protected readonly requestError = signal<string | null>(null);
  protected readonly maxRequestText = MAX_ATTEMPT_REQUEST_TEXT;

  constructor() {
    this.api.listMyExams().subscribe({
      next: (exams) => {
        this.exams.set(exams);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  /** Whether attempts need numbering: only when there can be, or already are, more than one. */
  protected showNumbers(exam: MyExamDto): boolean {
    return exam.attemptsAllowed > 1 || exam.attempts.length > 1;
  }

  /** The label of the button that begins the next attempt. */
  protected startLabel(exam: MyExamDto): string {
    return exam.attempts.length === 0 ? 'Start exam' : `Start attempt ${exam.attemptsUsed + 1}`;
  }

  protected openRequestForm(exam: MyExamDto): void {
    this.requestMessage.set('');
    this.requestError.set(null);
    this.requestingFor.set(exam.examId);
  }

  protected closeRequestForm(): void {
    this.requestingFor.set(null);
  }

  /** Sends the request, then reads the list again so the card shows it waiting, whatever else changed meanwhile. */
  protected sendRequest(exam: MyExamDto): void {
    if (this.requestBusy()) {
      return;
    }

    this.requestBusy.set(true);
    this.requestError.set(null);
    this.api.requestAttempt(exam.examId, this.requestMessage().trim() || null).subscribe({
      next: () => {
        this.requestBusy.set(false);
        this.requestingFor.set(null);
        this.reload();
      },
      error: (error: unknown) => {
        this.requestBusy.set(false);
        this.requestError.set(extractErrorMessage(error));
      },
    });
  }

  private reload(): void {
    this.api.listMyExams().subscribe({
      next: (exams) => this.exams.set(exams),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }

  /** Starts the attempt (the clock starts here, on the server) and opens it. */
  protected start(exam: MyExamDto): void {
    if (this.startingExamId() !== null) {
      return;
    }

    this.startingExamId.set(exam.examId);
    this.errorMessage.set(null);
    this.api.startAttempt(exam.examId).subscribe({
      next: (attempt) => {
        this.startingExamId.set(null);
        void this.router.navigate(['/attempt', attempt.id]);
      },
      error: (error: unknown) => {
        this.startingExamId.set(null);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
