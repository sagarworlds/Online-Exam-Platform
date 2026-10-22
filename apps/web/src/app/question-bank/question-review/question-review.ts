import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { Permission } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { QuestionApiService } from '../question-api.service';
import { QuestionStatus, ReviewEntry, ReviewResult, ReviewStep } from '../question.models';

/** How each status is written for a person. */
export const STATUS_LABELS: Record<QuestionStatus, string> = {
  draft: 'Draft',
  in_review: 'In review',
  approved: 'Approved',
  retired: 'Retired',
};

/** The message that says each status in the language in use. */
export const STATUS_KEYS: Record<QuestionStatus, MessageKey> = {
  draft: 'question.status.draft',
  in_review: 'question.status.in_review',
  approved: 'question.status.approved',
  retired: 'question.status.retired',
};

const KIND_KEYS: Record<ReviewEntry['kind'], MessageKey> = {
  commented: 'question.review.kind.commented',
  submitted: 'question.review.kind.submitted',
  approved: 'question.review.kind.approved',
  changes_requested: 'question.review.kind.changes_requested',
  retired: 'question.review.kind.retired',
  restored: 'question.review.kind.restored',
};

interface StepButton {
  step: ReviewStep;
  labelKey: MessageKey;
  /** The permission that allows it; the server enforces it too, this only keeps buttons nobody may press out of sight. */
  permission: string;
  /** Whether the step cannot be taken without a comment. */
  needsComment: boolean;
}

const STEPS_BY_STATUS: Record<QuestionStatus, readonly StepButton[]> = {
  draft: [
    { step: 'submit-for-review', labelKey: 'question.review.step.submitForReview', permission: Permission.QuestionManage, needsComment: false },
    { step: 'retire', labelKey: 'question.review.step.retire', permission: Permission.QuestionManage, needsComment: false },
  ],
  in_review: [
    { step: 'approve', labelKey: 'question.review.step.approve', permission: Permission.QuestionReview, needsComment: false },
    { step: 'request-changes', labelKey: 'question.review.step.sendBack', permission: Permission.QuestionReview, needsComment: true },
    { step: 'retire', labelKey: 'question.review.step.retire', permission: Permission.QuestionManage, needsComment: false },
  ],
  approved: [{ step: 'retire', labelKey: 'question.review.step.retire', permission: Permission.QuestionManage, needsComment: false }],
  retired: [{ step: 'restore', labelKey: 'question.review.step.restore', permission: Permission.QuestionManage, needsComment: false }],
};

/**
 * A question's review (FR-8): where it is in the workflow, the steps its status allows the signed-in user to take, and the thread
 * of comments and decisions. The thread is read the first time the panel is opened. It reports a change of status and leaves the
 * list to show it, so the card never has to know how the list is kept.
 */
@Component({
  selector: 'app-question-review',
  imports: [DatePipe, TranslatePipe],
  templateUrl: './question-review.html',
})
export class QuestionReview {
  readonly questionId = input.required<string>();
  readonly status = input.required<QuestionStatus>();

  /** The question's status changed; carries the new one. */
  readonly statusChanged = output<QuestionStatus>();

  private readonly api = inject(QuestionApiService);
  private readonly session = inject(AuthSessionService);

  protected readonly open = signal(false);
  protected readonly entries = signal<ReviewEntry[] | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly comment = signal('');

  protected readonly canComment = computed(() => this.session.hasPermission(Permission.QuestionRead));
  protected readonly steps = computed(() => STEPS_BY_STATUS[this.status()].filter((s) => this.session.hasPermission(s.permission)));
  protected readonly statusKeys = STATUS_KEYS;

  protected kindKey(kind: ReviewEntry['kind']): MessageKey {
    return KIND_KEYS[kind];
  }

  protected toggle(): void {
    this.open.update((open) => !open);
    if (this.open() && this.entries() === null) {
      this.api.reviewLog(this.questionId()).subscribe({
        next: (entries) => this.entries.set(entries),
        error: (error: unknown) => this.loadError.set(extractErrorMessage(error)),
      });
    }
  }

  protected onCommentInput(value: string): void {
    this.comment.set(value);
  }

  /** Whether the comment box is enough for this step: one that needs a reason is refused here before it is sent. */
  protected blocked(step: StepButton): boolean {
    return this.busy() || (step.needsComment && this.comment().trim() === '');
  }

  protected take(step: StepButton): void {
    this.run(this.api.reviewStep(this.questionId(), step.step, this.comment().trim()));
  }

  protected addComment(): void {
    this.run(this.api.comment(this.questionId(), this.comment().trim()));
  }

  private run(request: ReturnType<QuestionApiService['comment']>): void {
    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: (result: ReviewResult) => {
        this.entries.update((entries) => [...(entries ?? []), result.entry]);
        this.comment.set('');
        this.busy.set(false);
        if (result.status !== this.status()) {
          this.statusChanged.emit(result.status);
        }
      },
      error: (error: unknown) => {
        this.error.set(extractErrorMessage(error));
        this.busy.set(false);
      },
    });
  }
}
