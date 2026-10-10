import { DOCUMENT, DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage, extractProblemCode } from '../../shared/problem-details';
import { CandidateApiService } from '../candidate-api.service';
import { AnswerVerdict, AttemptReviewDto, DisputeStatus, MAX_DISPUTE_REASON, MyDisputeDto, ReviewQuestionDto } from '../candidate.models';
import { MathDirective } from '../../shared/rich-text/math.directive';

/** A question with its place in the exam, so the page can number it across sections. */
interface NumberedQuestion extends ReviewQuestionDto {
  number: number;
}

/**
 * Where a candidate reads which of their answers were right (FR-32, FR-33): every question with each option marked as the
 * correct one and as the one they chose, how it was marked, and the totals. The API only answers once the attempt is over
 * and the exam's author has released the answers; until then it says so and this page shows that reason.
 *
 * It is also where the candidate disputes an answer key (FR-31): one dispute per question, while the time allowed since the result was
 * released lasts, shown under the question with how staff answered it. The API decides whether a dispute is still allowed; the page
 * only offers it while the review says the window is open.
 */
@Component({
  selector: 'app-attempt-review',
  imports: [RouterLink, DatePipe, MathDirective, TranslatePipe],
  templateUrl: './attempt-review.html',
  styleUrl: './attempt-review.css',
})
export class AttemptReview {
  private readonly api = inject(CandidateApiService);
  private readonly i18n = inject(I18nService);
  private readonly document = inject(DOCUMENT);
  protected readonly attemptId = inject(ActivatedRoute).snapshot.paramMap.get('attemptId');

  protected readonly review = signal<AttemptReviewDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  /** The answers are held until the exam's author releases them; {@link availableFromUtc} says when, if that is decided. */
  protected readonly locked = signal(false);
  protected readonly availableFromUtc = signal<string | null>(null);

  /** The candidate's disputes of this attempt: those the review came with, and any raised since without reloading. */
  protected readonly disputes = signal<MyDisputeDto[]>([]);
  /** The question whose dispute form is open; one at a time. */
  protected readonly disputingId = signal<string | null>(null);
  protected readonly disputeReason = signal('');
  protected readonly disputeBusy = signal(false);
  protected readonly disputeError = signal<string | null>(null);
  /** The question whose dispute was just sent, so its card says so (announced to screen readers). */
  protected readonly disputeSentId = signal<string | null>(null);
  protected readonly maxDisputeReason = MAX_DISPUTE_REASON;

  /** A question can be disputed once per attempt, so each has at most one dispute. */
  private readonly disputeByQuestion = computed(() => new Map(this.disputes().map((dispute) => [dispute.questionId, dispute])));

  constructor() {
    if (this.attemptId === null) {
      this.loading.set(false);
      this.errorMessage.set('No attempt was given.');
      return;
    }
    this.load();
  }

  /** Reads the review again, e.g. after a failed read. */
  protected retry(): void {
    this.load();
  }

  private load(): void {
    const attemptId = this.attemptId;
    if (attemptId === null) {
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);
    this.locked.set(false);
    this.api.getAttemptReview(attemptId).subscribe({
      next: (review) => {
        this.review.set(review);
        this.disputes.set(review.disputes ?? []);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        // Held answers are an expected state: the page shows when they open rather than an error.
        if (extractProblemCode(error) === 'results_not_released') {
          this.loadReleaseTime(attemptId);
          return;
        }
        this.loading.set(false);
        // The API's own reason is the most useful thing to tell the candidate; the page offers a retry.
        this.errorMessage.set(extractErrorMessage(error, 'The answers could not be loaded. Please try again.'));
      },
    });
  }

  /**
   * Reads when held answers open from the attempt, which the API answers without the release check. The date is a convenience: if that read
   * fails the page still says the answers are held, without the date, and the failure is logged rather than hidden.
   */
  private loadReleaseTime(attemptId: string): void {
    this.api.getAttempt(attemptId).subscribe({
      next: (attempt) => {
        this.availableFromUtc.set(attempt.review?.availableFromUtc ?? null);
        this.locked.set(true);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        console.error('Reading when the answers open failed', error);
        this.availableFromUtc.set(null);
        this.locked.set(true);
        this.loading.set(false);
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

  /** The candidate's dispute of this question's answer key, if they raised one. */
  protected disputeFor(questionId: string): MyDisputeDto | undefined {
    return this.disputeByQuestion().get(questionId);
  }

  protected disputeStatusLabel(status: DisputeStatus): string {
    switch (status) {
      case 'Accepted':
        return this.i18n.t('review.dispute.status.accepted');
      case 'Rejected':
        return this.i18n.t('review.dispute.status.rejected');
      default:
        return this.i18n.t('review.dispute.status.open');
    }
  }

  protected startDispute(questionId: string): void {
    this.disputeReason.set('');
    this.disputeError.set(null);
    this.disputeSentId.set(null);
    this.disputingId.set(questionId);
  }

  protected cancelDispute(): void {
    this.disputingId.set(null);
  }

  /** Sends the dispute and, once the API has taken it, shows it under the question without reading the review again. */
  protected sendDispute(questionId: string): void {
    const reason = this.disputeReason().trim();
    if (this.attemptId === null || reason === '' || this.disputeBusy()) {
      return;
    }

    this.disputeBusy.set(true);
    this.disputeError.set(null);
    this.api.raiseDispute(this.attemptId, questionId, reason).subscribe({
      next: (dispute) => {
        this.disputeBusy.set(false);
        this.disputes.update((list) => [...list, dispute]);
        this.disputingId.set(null);
        this.disputeSentId.set(questionId);
      },
      error: (error: unknown) => {
        this.disputeBusy.set(false);
        this.disputeError.set(this.disputeErrorMessage(error));
      },
    });
  }

  /** The two refusals a candidate can cause are worded here, in their language; any other is the API's own sentence, e.g. when the time to dispute ended. */
  private disputeErrorMessage(error: unknown): string {
    switch (extractProblemCode(error)) {
      case 'dispute_already_raised':
        return this.i18n.t('review.dispute.error.alreadyRaised');
      case 'invalid_attempt':
        return this.i18n.t('review.dispute.error.invalid', { max: MAX_DISPUTE_REASON });
      default:
        return extractErrorMessage(error, this.i18n.t('review.dispute.error.generic'));
    }
  }
}
