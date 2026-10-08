import { Component, inject, input, signal } from '@angular/core';
import { extractErrorMessage, extractProblemCode } from '../../shared/problem-details';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { CandidateApiService } from '../candidate-api.service';
import { ISSUE_CATEGORIES, IssueCategory, MAX_ISSUE_MESSAGE } from '../candidate.models';

/**
 * The "report an issue" button of the exam page (FR-42). A candidate says what is wrong with a question, the page or anything else
 * without leaving the exam: the form opens in place, the exam behind it carries on and its clock keeps running, and staff read the
 * report in a queue. It sits outside the part of the page that a pause replaces, so a candidate who was stopped can still say why that
 * is a problem.
 */
@Component({
  selector: 'app-report-issue',
  imports: [TranslatePipe],
  templateUrl: './report-issue.html',
})
export class ReportIssue {
  private readonly api = inject(CandidateApiService);
  private readonly i18n = inject(I18nService);

  /** The attempt being sat. */
  readonly attemptId = input.required<string>();

  /** The question on screen, or null when none is; a report of the "question" kind names it so staff know which one. */
  readonly questionId = input<string | null>(null);

  protected readonly categories = ISSUE_CATEGORIES;
  protected readonly maxMessage = MAX_ISSUE_MESSAGE;

  protected readonly open = signal(false);
  protected readonly category = signal<IssueCategory>('Technical');
  protected readonly message = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  /** Whether the last report went through, so the page says so (announced to screen readers) in place of the form. */
  protected readonly sent = signal(false);

  protected categoryLabel(category: IssueCategory): string {
    switch (category) {
      case 'Question':
        return this.i18n.t('attempt.report.category.Question');
      case 'Technical':
        return this.i18n.t('attempt.report.category.Technical');
      default:
        return this.i18n.t('attempt.report.category.Other');
    }
  }

  /** Opens an empty form, leaning towards "question" when one is on screen since that is the likeliest thing a candidate wants to report. */
  protected start(): void {
    this.category.set(this.questionId() === null ? 'Technical' : 'Question');
    this.message.set('');
    this.error.set(null);
    this.sent.set(false);
    this.open.set(true);
  }

  protected cancel(): void {
    this.open.set(false);
  }

  /** Sends the report and, once the API has taken it, closes the form and says so. Whatever the candidate wrote is kept if it fails. */
  protected send(): void {
    const message = this.message().trim();
    if (message === '' || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    const category = this.category();
    this.api.reportIssue(this.attemptId(), category, message, category === 'Question' ? this.questionId() : null).subscribe({
      next: () => {
        this.busy.set(false);
        this.open.set(false);
        this.sent.set(true);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(this.errorMessage(error));
      },
    });
  }

  /** The refusals a candidate can cause are worded here, in their language; any other is the API's own sentence, e.g. when the attempt is over. */
  private errorMessage(error: unknown): string {
    switch (extractProblemCode(error)) {
      case 'invalid_attempt':
        return this.i18n.t('attempt.report.error.invalid', { max: MAX_ISSUE_MESSAGE });
      case 'too_many_issue_reports':
        return this.i18n.t('attempt.report.error.tooMany');
      default:
        return extractErrorMessage(error, this.i18n.t('attempt.report.error.generic'));
    }
  }
}
