import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { bestAttemptId } from '../best-attempt';
import { MAX_ATTEMPT_REQUEST_TEXT, MyExamDto } from '../candidate.models';

/**
 * The candidate's page: the exams they have accepted an invitation to, and whether each can be started now (FR-16). Starting leads to
 * the instructions and system check page (FR-17); the attempt itself begins there, once the candidate has acknowledged the instructions.
 */
@Component({
  selector: 'app-my-exams',
  imports: [DatePipe, RouterLink, TranslatePipe],
  templateUrl: './my-exams.html',
})
export class MyExams {
  private readonly api = inject(CandidateApiService);
  private readonly i18n = inject(I18nService);

  protected readonly exams = signal<MyExamDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
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
    return exam.attempts.length === 0 ? this.i18n.t('myExams.start') : this.i18n.t('myExams.startAttempt', { number: exam.attemptsUsed + 1 });
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
}
