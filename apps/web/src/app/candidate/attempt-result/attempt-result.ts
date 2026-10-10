import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { AttemptResultDto } from '../candidate.models';

/**
 * Where a candidate reads their result (FR-32): the score, where it stands among the exam's released results (rank and percentile), and the
 * marks by section. The API answers only once the exam's author has released the results; until then it says so, and this page shows that
 * reason. The answers themselves are on the review page, which is linked from here.
 */
@Component({
  selector: 'app-attempt-result',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './attempt-result.html',
  styleUrl: './attempt-result.css',
})
export class AttemptResult {
  private readonly api = inject(CandidateApiService);
  private readonly i18n = inject(I18nService);
  protected readonly attemptId = inject(ActivatedRoute).snapshot.paramMap.get('attemptId');

  protected readonly result = signal<AttemptResultDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    if (this.attemptId === null) {
      this.loading.set(false);
      this.errorMessage.set(this.i18n.t('result.noAttempt'));
      return;
    }

    this.api.getAttemptResult(this.attemptId).subscribe({
      next: (result) => {
        this.result.set(result);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        // The API's own reason, e.g. "not released yet, shown from ...", is the most useful thing to tell the candidate.
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('result.loadError')));
      },
    });
  }

  /** `+4`, `0` or `-1`: the sign is always shown so a gain and a loss cannot be mistaken for each other. */
  protected formatMarks(marks: number): string {
    return marks > 0 ? `+${marks}` : `${marks}`;
  }
}
