import { DOCUMENT, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { AnswerVerdict, AttemptReviewDto, ReviewQuestionDto } from '../candidate.models';
import { MathDirective } from '../../shared/rich-text/math.directive';

/** A question with its place in the exam, so the page can number it across sections. */
interface NumberedQuestion extends ReviewQuestionDto {
  number: number;
}

/**
 * Where a candidate reads which of their answers were right (FR-32, FR-33): every question with each option marked as the
 * correct one and as the one they chose, how it was marked, and the totals. The API only answers once the attempt is over
 * and the exam's author has released the answers; until then it says so and this page shows that reason.
 */
@Component({
  selector: 'app-attempt-review',
  imports: [RouterLink, DatePipe, MathDirective, TranslatePipe],
  templateUrl: './attempt-review.html',
})
export class AttemptReview {
  private readonly api = inject(CandidateApiService);
  private readonly i18n = inject(I18nService);
  private readonly document = inject(DOCUMENT);
  protected readonly attemptId = inject(ActivatedRoute).snapshot.paramMap.get('attemptId');

  protected readonly review = signal<AttemptReviewDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    if (this.attemptId === null) {
      this.loading.set(false);
      this.errorMessage.set('No attempt was given.');
      return;
    }

    this.api.getAttemptReview(this.attemptId).subscribe({
      next: (review) => {
        this.review.set(review);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        // The API's own reason, e.g. "not released yet, shown from ...", is the most useful thing to tell the candidate.
        this.errorMessage.set(extractErrorMessage(error, 'The answers could not be loaded. Please try again.'));
      },
    });
  }

  /** The review's questions numbered across sections, each section keeping its own heading. */
  protected sections(review: AttemptReviewDto): { id: string; name: string; questions: NumberedQuestion[] }[] {
    let number = 0;
    return review.sections.map((section) => ({
      id: section.id,
      name: section.name,
      questions: section.questions.map((question) => ({ ...question, number: ++number })),
    }));
  }

  protected optionLetter(index: number): string {
    return String.fromCharCode(65 + index);
  }

  /** `+4`, `0` or `-1`: the sign is always shown so a gain and a loss cannot be mistaken for each other. */
  protected formatMarks(marks: number): string {
    return marks > 0 ? `+${marks}` : `${marks}`;
  }

  protected verdictLabel(verdict: AnswerVerdict): string {
    switch (verdict) {
      case 'Correct':
        return this.i18n.t('verdict.correct');
      case 'Partial':
        return this.i18n.t('verdict.partial');
      case 'Wrong':
        return this.i18n.t('verdict.wrong');
      default:
        return this.i18n.t('verdict.unanswered');
    }
  }

  protected scrollTo(number: number): void {
    this.document.getElementById(`review-question-${number}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
}
