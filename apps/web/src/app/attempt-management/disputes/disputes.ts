import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { Permission } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { AnswerKeyCorrection } from '../../question-bank/answer-key-correction/answer-key-correction';
import { AnswerKeyCorrectionResult } from '../../question-bank/question.models';
import { extractErrorMessage } from '../../shared/problem-details';
import { MathDirective } from '../../shared/rich-text/math.directive';
import { AttemptAdminApiService } from '../attempt-admin-api.service';
import { DisputeFilterStatus, DisputeRow, MAX_DISPUTE_REJECTION_NOTE } from '../attempt-admin.models';

/** The disputes about one question, so staff see how many candidates doubt the same answer key. */
interface DisputeGroup {
  questionId: string;
  /** Sanitized HTML of the question as it is now; null when the bank no longer has it. */
  questionText: string | null;
  disputes: DisputeRow[];
}

/**
 * Admin page: candidates' disputes of an answer key (FR-31), oldest first and gathered by question. A dispute is settled in one of two
 * ways. Staff with `question.manage` correct the question's answer key, which rescores every result it affects and accepts all the open
 * disputes of that question at once; anyone with `exam.manage` can instead reject a dispute with an explanation the candidate sees.
 * The API decides whether either is still possible and this page shows its reason instead of guessing.
 */
@Component({
  selector: 'app-disputes',
  imports: [DatePipe, RouterLink, MathDirective, AnswerKeyCorrection],
  templateUrl: './disputes.html',
})
export class Disputes {
  private readonly api = inject(AttemptAdminApiService);
  private readonly session = inject(AuthSessionService);
  /** The read of the queue in flight, so a newer read (another status chosen meanwhile) is never overwritten by an older one. */
  private listing: Subscription | null = null;

  protected readonly disputes = signal<DisputeRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly status = signal<DisputeFilterStatus>('open');
  protected readonly statuses: readonly { value: DisputeFilterStatus; label: string }[] = [
    { value: 'open', label: 'Open' },
    { value: 'accepted', label: 'Accepted' },
    { value: 'rejected', label: 'Rejected' },
  ];

  /** The dispute a call is running for, so only its buttons are locked meanwhile. */
  protected readonly busyId = signal<string | null>(null);
  /** Why the last call about a dispute failed, by dispute id, so the reason shows on that dispute. */
  protected readonly rowErrors = signal<Record<string, string>>({});

  /** The dispute whose rejection form is open; one at a time, so a stray click cannot reject two. */
  protected readonly rejectingId = signal<string | null>(null);
  protected readonly note = signal('');
  protected readonly maxNoteLength = MAX_DISPUTE_REJECTION_NOTE;

  /** The question whose answer-key panel is open; one at a time. */
  protected readonly correctingQuestionId = signal<string | null>(null);
  /** Whether the signed-in user may correct an answer key; the API checks it again, this only keeps the action from those who may not. */
  protected readonly canCorrectKey = computed(() => this.session.hasPermission(Permission.QuestionManage));

  /** The disputes by question, in the order of each question's oldest dispute, each question's disputes oldest first. */
  protected readonly groups = computed<DisputeGroup[]>(() => {
    const byQuestion = new Map<string, DisputeGroup>();
    for (const dispute of this.disputes()) {
      const group = byQuestion.get(dispute.questionId) ?? { questionId: dispute.questionId, questionText: dispute.questionText, disputes: [] };
      group.disputes.push(dispute);
      byQuestion.set(dispute.questionId, group);
    }
    return [...byQuestion.values()];
  });

  constructor() {
    this.fetch();
  }

  protected onStatusChanged(value: string): void {
    const status = this.statuses.find((s) => s.value === value)?.value;
    if (status === undefined || status === this.status()) {
      return;
    }

    this.status.set(status);
    this.rejectingId.set(null);
    this.correctingQuestionId.set(null);
    this.notice.set(null);
    this.loading.set(true);
    this.errorMessage.set(null);
    this.disputes.set([]);
    this.fetch();
  }

  /** What to call a group: those still waiting are candidates who dispute the question; settled ones are disputes that were answered. */
  protected heading(group: DisputeGroup): string {
    const count = group.disputes.length;
    if (this.status() === 'open') {
      return count === 1 ? '1 candidate disputes this question' : `${count} candidates dispute this question`;
    }

    return count === 1 ? '1 dispute about this question' : `${count} disputes about this question`;
  }

  protected emptyMessage(): string {
    return `No ${this.status()} disputes.`;
  }

  protected startRejecting(dispute: DisputeRow): void {
    this.note.set('');
    this.rejectingId.set(dispute.id);
  }

  protected cancelRejecting(): void {
    this.rejectingId.set(null);
  }

  protected confirmReject(dispute: DisputeRow): void {
    const note = this.note().trim();
    if (this.busyId() !== null || note === '') {
      return;
    }

    this.busyId.set(dispute.id);
    this.notice.set(null);
    this.rowErrors.update((errors) => Object.fromEntries(Object.entries(errors).filter(([id]) => id !== dispute.id)));
    this.api.rejectDispute(dispute.id, note).subscribe({
      next: () => {
        this.busyId.set(null);
        this.rejectingId.set(null);
        this.notice.set(`Rejected the dispute from ${dispute.candidateEmail ?? 'the candidate'}. They will see your explanation.`);
        this.fetch();
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.rowErrors.update((errors) => ({ ...errors, [dispute.id]: extractErrorMessage(error) }));
      },
    });
  }

  protected startCorrecting(group: DisputeGroup): void {
    this.notice.set(null);
    this.correctingQuestionId.set(group.questionId);
  }

  protected cancelCorrecting(): void {
    this.correctingQuestionId.set(null);
  }

  /** Says what the correction did, then reads the queue again: the disputes it accepted have left it. */
  protected onCorrected(result: AnswerKeyCorrectionResult): void {
    this.correctingQuestionId.set(null);
    this.notice.set(
      result.keyChanged
        ? `Answer key corrected; ${result.attemptsRescored} ${result.attemptsRescored === 1 ? 'result' : 'results'} rescored; the disputes about this question were accepted.`
        : 'The answer key was already that, so nothing was changed and no results were rescored.',
    );
    this.fetch();
  }

  /** Reads the queue for the chosen status. After an action the list stays up meanwhile, so the page does not jump. */
  private fetch(): void {
    this.listing?.unsubscribe();
    this.listing = this.api.listDisputes(this.status()).subscribe({
      next: (disputes) => {
        this.disputes.set(disputes);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
