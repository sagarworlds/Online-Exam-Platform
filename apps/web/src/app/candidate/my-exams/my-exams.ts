import { DatePipe } from '@angular/common';
import { Component, ElementRef, Injector, afterNextRender, inject, signal } from '@angular/core';
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
  styleUrl: './my-exams.css',
})
export class MyExams {
  private readonly api = inject(CandidateApiService);
  private readonly i18n = inject(I18nService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

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

  /** The extra time the candidate's accommodation gives, in words (FR-49); null when it gives none. */
  protected extraTime(exam: MyExamDto): string | null {
    const seconds = exam.accommodation?.extraTimeSeconds ?? 0;
    return seconds > 0 ? this.i18n.plural('time.minutes', Math.round(seconds / 60)) : null;
  }

  /** The label of the button that begins the next attempt. */
  protected startLabel(exam: MyExamDto): string {
    return exam.attempts.length === 0 ? this.i18n.t('myExams.start') : this.i18n.t('myExams.startAttempt', { number: exam.attemptsUsed + 1 });
  }

  /**
   * The attempt the candidate started and has not yet submitted, so they can carry on with it; null when there is none.
   * Only one attempt can be open at a time, so the first match is the one.
   */
  protected resumableAttemptId(exam: MyExamDto): string | null {
    return exam.attempts.find((attempt) => attempt.status === 'InProgress')?.id ?? null;
  }

  /** Whether the card offers "ask for another attempt" now: the API allows it and the request form is not already open. */
  protected canAskForAttempt(exam: MyExamDto): boolean {
    return exam.canRequestAttempt && this.requestingFor() !== exam.examId;
  }

  /** Whether the card has an action row to show: resume, start, or ask for another attempt. */
  protected hasActions(exam: MyExamDto): boolean {
    return this.resumableAttemptId(exam) !== null || exam.canStartAttempt || this.canAskForAttempt(exam);
  }

  /** Opens the form in place of the button that asked for it, and puts focus in its first field, where the candidate starts. */
  protected openRequestForm(exam: MyExamDto): void {
    this.requestMessage.set('');
    this.requestError.set(null);
    this.requestingFor.set(exam.examId);
    afterNextRender(() => this.element(`request-message-${exam.examId}`)?.focus(), { injector: this.injector });
  }

  /** Puts the form away without sending anything, and gives focus back to the button that opened it. */
  protected closeRequestForm(exam: MyExamDto): void {
    this.requestingFor.set(null);
    afterNextRender(() => this.element(`request-attempt-${exam.examId}`)?.focus(), { injector: this.injector });
  }

  /**
   * Sends the request, then reads the list again so the card shows it waiting, whatever else changed meanwhile. The send button is disabled
   * while the request runs, and a disabled control loses focus, so focus is put back on it when the request is refused.
   */
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
        // The form goes, and the button that opened it goes once the list is read again, so focus moves to the exam's title that stays.
        afterNextRender(() => this.element(`exam-title-${exam.examId}`)?.focus(), { injector: this.injector });
        this.reload();
      },
      error: (error: unknown) => {
        this.requestBusy.set(false);
        this.requestError.set(extractErrorMessage(error));
        afterNextRender(() => this.element(`request-send-${exam.examId}`)?.focus(), { injector: this.injector });
      },
    });
  }

  private reload(): void {
    this.api.listMyExams().subscribe({
      next: (exams) => this.exams.set(exams),
      error: (error: unknown) => this.errorMessage.set(extractErrorMessage(error)),
    });
  }

  /**
   * The element with this id on the page. Ids here come from the exam's id, which is a GUID that may start with a digit, so they are matched
   * as attribute values: a CSS id selector would not parse.
   */
  private element<T extends HTMLElement = HTMLElement>(id: string): T | null {
    return this.host.nativeElement.querySelector<T>(`[id="${id}"]`);
  }
}
