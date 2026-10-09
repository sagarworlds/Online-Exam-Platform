import { Component, ElementRef, Injector, afterNextRender, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { extractErrorMessage, extractProblemCode } from '../../shared/problem-details';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { CandidateApiService } from '../candidate-api.service';
import { IssueCategory, MAX_ISSUE_MESSAGE } from '../candidate.models';

/** What a report is about: the question on screen, or the attempt as a whole. */
export type ReportScope = 'question' | 'attempt';

/**
 * The kinds of report about the attempt as a whole. A report about a question needs no kind: it is always about the question on screen,
 * so the candidate is not asked to choose one.
 */
const ATTEMPT_CATEGORIES: readonly IssueCategory[] = ['Technical', 'Other'];

/**
 * The report control of the exam page (FR-42), in two places. Beside a question's own buttons it reports a problem with that question,
 * and names the question. The attempt-wide control reports a technical problem or anything else, and it is the one a candidate still has
 * while the attempt is paused. The form opens in place, the exam behind it carries on and its clock keeps running, and staff read the
 * report in a queue.
 */
@Component({
  selector: 'app-report-issue',
  imports: [TranslatePipe],
  templateUrl: './report-issue.html',
  styleUrl: './report-issue.css',
  host: {
    '[class.report-issue--question]': "scope() === 'question'",
    '[class.report-issue--expanded]': 'open() || sent()',
  },
})
export class ReportIssue {
  private readonly api = inject(CandidateApiService);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The attempt being sat. */
  readonly attemptId = input.required<string>();

  /** What the report is about. A question report names the question on screen; an attempt report never names one. */
  readonly scope = input<ReportScope>('attempt');

  /** The question on screen, or null when none is. */
  readonly questionId = input<string | null>(null);

  protected readonly attemptCategories = ATTEMPT_CATEGORIES;
  protected readonly maxMessage = MAX_ISSUE_MESSAGE;

  protected readonly open = signal(false);
  protected readonly category = signal<IssueCategory>('Technical');
  protected readonly message = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  /** Whether the last report went through, so the page says so (announced to screen readers) in place of the form. */
  protected readonly sent = signal(false);
  /** The question the form, or the last report, was started on. A question report is sent about this question, whatever is on screen by then. */
  private readonly reportedQuestionId = signal<string | null>(null);

  /** Prefixes the form's element ids, so the attempt-wide and the per-question controls can both be open without clashing. */
  protected readonly idPrefix = computed(() => (this.scope() === 'question' ? 'report-issue-question' : 'report-issue'));

  private readonly openButton = viewChild<ElementRef<HTMLButtonElement>>('openButton');
  private readonly messageBox = viewChild<ElementRef<HTMLTextAreaElement>>('messageBox');

  constructor() {
    // A question report belongs to the question it was started on. When the candidate moves to another question, the form and any note
    // about the last report are put away, so nothing typed for one question is ever sent about another.
    effect(() => {
      const current = this.questionId();
      untracked(() => {
        if (this.reportedQuestionId() !== current) {
          this.reset();
        }
      });
    });
  }

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

  /**
   * Opens an empty form, and moves focus into it. A question report needs a question on screen; without one there is nothing to name,
   * so it does not open.
   */
  protected start(): void {
    const questionId = this.scope() === 'question' ? this.questionId() : null;
    if (this.scope() === 'question' && questionId === null) {
      return;
    }

    this.reportedQuestionId.set(questionId);
    this.category.set('Technical');
    this.message.set('');
    this.error.set(null);
    this.sent.set(false);
    this.open.set(true);
    afterNextRender(() => this.focusMessage(), { injector: this.injector });
  }

  /**
   * Moves focus into the form. A question's form first brings that question up to the top of the screen, so the question and the form
   * under it can both be seen while the candidate writes. Focus is then placed without scrolling, since scrolling to the box would take
   * the question back out of view.
   */
  private focusMessage(): void {
    const box = this.messageBox()?.nativeElement;
    if (box === undefined) {
      return;
    }

    if (this.scope() === 'question') {
      this.host.nativeElement.closest('.exam-question')?.querySelector<HTMLElement>('.question__text')?.scrollIntoView({ block: 'start' });
      box.focus({ preventScroll: true });
    } else {
      box.focus();
    }
  }

  /** Puts the form away without sending anything, and gives focus back to the button that opened it. */
  protected cancel(): void {
    this.open.set(false);
    afterNextRender(() => this.openButton()?.nativeElement.focus(), { injector: this.injector });
  }

  /** Sends the report and, once the API has taken it, closes the form and says so. Whatever the candidate wrote is kept if it fails. */
  protected send(): void {
    const message = this.message().trim();
    if (message === '' || this.busy()) {
      return;
    }

    const isQuestion = this.scope() === 'question';
    const category: IssueCategory = isQuestion ? 'Question' : this.category();
    const questionId = isQuestion ? this.reportedQuestionId() : null;

    this.busy.set(true);
    this.error.set(null);
    this.api.reportIssue(this.attemptId(), category, message, questionId).subscribe({
      next: () => {
        this.busy.set(false);
        this.open.set(false);
        this.sent.set(true);
        afterNextRender(() => this.openButton()?.nativeElement.focus(), { injector: this.injector });
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(this.errorMessage(error));
      },
    });
  }

  /** Clears the form and any note about a report, back to the state of a control that has not been used. */
  private reset(): void {
    this.reportedQuestionId.set(null);
    this.open.set(false);
    this.message.set('');
    this.error.set(null);
    this.sent.set(false);
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
