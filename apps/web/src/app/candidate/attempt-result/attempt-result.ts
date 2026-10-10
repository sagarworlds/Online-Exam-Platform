import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage, extractProblemCode } from '../../shared/problem-details';
import { DatePipe } from '@angular/common';
import { CandidateApiService } from '../candidate-api.service';
import { AttemptResultDto } from '../candidate.models';

/**
 * What the result page shows at any moment: the result is being read, it is here, it is held until the exam's author releases it, or
 * it could not be read and the candidate may try again.
 */
export type ResultView = 'loading' | 'ready' | 'locked' | 'error';

/**
 * Where a candidate reads their result (FR-32): the score, where it stands among the exam's released results (rank and percentile), and the
 * marks by section. The API answers only once the exam's author has released the results. Until then this page says the result is held, and
 * when it opens if that is already decided, and the answers stay on the review page, which is linked from here.
 */
@Component({
  selector: 'app-attempt-result',
  imports: [DatePipe, RouterLink, TranslatePipe],
  templateUrl: './attempt-result.html',
  styleUrl: './attempt-result.css',
})
export class AttemptResult {
  private readonly api = inject(CandidateApiService);
  private readonly i18n = inject(I18nService);
  protected readonly attemptId = inject(ActivatedRoute).snapshot.paramMap.get('attemptId');

  protected readonly view = signal<ResultView>('loading');
  protected readonly result = signal<AttemptResultDto | null>(null);
  /** When a held result opens, if the exam's author has already decided it (Scheduled); null when released by hand or not yet known. */
  protected readonly availableFromUtc = signal<string | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    if (this.attemptId === null) {
      this.errorMessage.set(this.i18n.t('result.noAttempt'));
      this.view.set('error');
      return;
    }
    this.load();
  }

  /** Reads the result again, e.g. after a failed read. */
  protected retry(): void {
    this.load();
  }

  private load(): void {
    const attemptId = this.attemptId;
    if (attemptId === null) {
      return;
    }

    this.view.set('loading');
    this.errorMessage.set(null);
    this.api.getAttemptResult(attemptId).subscribe({
      next: (result) => {
        this.result.set(result);
        this.view.set('ready');
      },
      error: (error: unknown) => {
        // A held result is an expected state, not a failure: the page says so and when it opens.
        if (extractProblemCode(error) === 'results_not_released') {
          this.loadReleaseTime(attemptId);
          return;
        }
        // The API's own reason is the most useful thing to show the candidate; the page offers a retry.
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('result.loadError')));
        this.view.set('error');
      },
    });
  }

  /**
   * Reads when a held result opens from the attempt, which the API answers without the release check. The date is a convenience: if that
   * read fails the page still says the result is held, just without the date, and the failure is logged rather than hidden.
   */
  private loadReleaseTime(attemptId: string): void {
    this.api.getAttempt(attemptId).subscribe({
      next: (attempt) => {
        this.availableFromUtc.set(attempt.review?.availableFromUtc ?? null);
        this.view.set('locked');
      },
      error: (error: unknown) => {
        console.error('Reading when the result opens failed', error);
        this.availableFromUtc.set(null);
        this.view.set('locked');
      },
    });
  }

  /** `+4`, `0` or `-1`: the sign is always shown so a gain and a loss cannot be mistaken for each other. */
  protected formatMarks(marks: number): string {
    return marks > 0 ? `+${marks}` : `${marks}`;
  }
}
