import { DatePipe } from '@angular/common';
import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChildren,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { Permission } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { DisputeStatus } from '../../candidate/candidate.models';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { AnswerKeyCorrection } from '../../question-bank/answer-key-correction/answer-key-correction';
import { AnswerKeyCorrectionResult } from '../../question-bank/question.models';
import { extractErrorMessage } from '../../shared/problem-details';
import { MathDirective } from '../../shared/rich-text/math.directive';
import { AttemptAdminApiService } from '../attempt-admin-api.service';
import {
  DisputeFilterStatus,
  DisputeRow,
  MAX_DISPUTE_REJECTION_NOTE,
} from '../attempt-admin.models';

/** The status a dispute got from a decision made on this page: the two ways it can be settled. */
type DecidedStatus = Exclude<DisputeStatus, 'Open'>;

/** The disputes about one question, and how many of them still wait for a decision (open on the server and not decided here). */
interface DisputeGroup {
  questionId: string;
  /** Sanitized HTML of the question as it is now; null when the bank no longer has it. */
  questionText: string | null;
  disputes: DisputeRow[];
  open: number;
}

const STATUS_KEYS: Readonly<Record<DisputeStatus, MessageKey>> = {
  Open: 'admin.disputes.status.Open',
  Accepted: 'admin.disputes.status.Accepted',
  Rejected: 'admin.disputes.status.Rejected',
};

const EMPTY_KEYS: Readonly<Record<DisputeFilterStatus, MessageKey>> = {
  open: 'admin.disputes.noneOpen',
  accepted: 'admin.disputes.noneAccepted',
  rejected: 'admin.disputes.noneRejected',
};

/**
 * Admin page: candidates' disputes of an answer key (FR-31), oldest first and gathered by question. A dispute is settled in one of two
 * ways. Staff with `question.manage` correct the question's answer key, which rescores every result it affects and accepts all the open
 * disputes of that question at once; anyone with `exam.manage` can instead reject a dispute with an explanation the candidate sees.
 * The API decides whether either is still possible and this page shows its reason instead of guessing.
 *
 * A decision does not take the dispute off the page. Its row becomes a line saying what was done, in the same place, and focus moves to
 * it. A correction says up front, on its button, how many disputes it accepts. The lines stay until the page is left or reloaded.
 */
@Component({
  selector: 'app-disputes',
  imports: [DatePipe, RouterLink, MathDirective, AnswerKeyCorrection, TranslatePipe],
  templateUrl: './disputes.html',
  styleUrl: './disputes.css',
})
export class Disputes {
  private readonly api = inject(AttemptAdminApiService);
  private readonly session = inject(AuthSessionService);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  /** The elements focus can be moved to, each marked with its `data-focus` key, so focus lands where the person was working. */
  private readonly focusTargets = viewChildren<ElementRef<HTMLElement>>('focusTarget');
  /** The read of the queue in flight, so a newer read (another status chosen meanwhile) is never overwritten by an older one. */
  private listing: Subscription | null = null;

  protected readonly disputes = signal<DisputeRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly status = signal<DisputeFilterStatus>('open');
  protected readonly statuses: readonly { value: DisputeFilterStatus; label: MessageKey }[] = [
    { value: 'open', label: 'admin.disputes.filter.open' },
    { value: 'accepted', label: 'admin.disputes.filter.accepted' },
    { value: 'rejected', label: 'admin.disputes.filter.rejected' },
  ];

  /** The decision made on each dispute in this visit, by dispute id; its line stands where its row was until the page is left. */
  protected readonly decisions = signal<Readonly<Record<string, DecidedStatus>>>({});
  /** The result of each correction made in this visit, by question id; its line stands above that question's disputes. */
  protected readonly corrections = signal<Readonly<Record<string, AnswerKeyCorrectionResult>>>({});

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
  protected readonly canCorrectKey = computed(() =>
    this.session.hasPermission(Permission.QuestionManage),
  );

  /** The disputes by question, in the order of each question's oldest dispute, each question's disputes oldest first. */
  protected readonly groups = computed<DisputeGroup[]>(() => {
    const decided = this.decisions();
    const byQuestion = new Map<string, DisputeGroup>();
    for (const dispute of this.disputes()) {
      const group = byQuestion.get(dispute.questionId) ?? {
        questionId: dispute.questionId,
        questionText: dispute.questionText,
        disputes: [],
        open: 0,
      };
      group.disputes.push(dispute);
      if (dispute.status === 'Open' && decided[dispute.id] === undefined) {
        group.open++;
      }
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

    // Decisions belong to the list they were made on, so a new list starts clean.
    this.status.set(status);
    this.rejectingId.set(null);
    this.correctingQuestionId.set(null);
    this.decisions.set({});
    this.corrections.set({});
    this.loading.set(true);
    this.errorMessage.set(null);
    this.disputes.set([]);
    this.fetch();
  }

  /** What to call a group: while disputes are open, how many still wait; otherwise, how many were about the question. */
  protected heading(group: DisputeGroup): string {
    if (this.status() !== 'open') {
      return this.i18n.plural('admin.disputes.pastHeading', group.disputes.length);
    }
    return group.open === 0
      ? this.i18n.t('admin.disputes.allSettled')
      : this.i18n.plural('admin.disputes.openHeading', group.open);
  }

  /** The button that opens the answer-key panel, saying how many open disputes the correction accepts. */
  protected correctButton(group: DisputeGroup): string {
    return this.i18n.plural('admin.disputes.correctOpen', group.open);
  }

  protected emptyMessage(): string {
    return this.i18n.t(EMPTY_KEYS[this.status()]);
  }

  protected statusLabel(status: DisputeStatus): string {
    return this.i18n.t(STATUS_KEYS[status]);
  }

  /** The candidate's address, or "the candidate" in a sentence when it is not known. */
  protected candidateName(dispute: DisputeRow): string {
    return dispute.candidateEmail ?? this.i18n.t('admin.disputes.candidateFallback');
  }

  /** The line that stands for a dispute decided here: what was done to it. */
  protected decisionText(dispute: DisputeRow, decided: DecidedStatus): string {
    const who = this.candidateName(dispute);
    return decided === 'Accepted'
      ? this.i18n.t('admin.disputes.acceptedResult', { who })
      : this.i18n.t('admin.disputes.rejectedResult', { who });
  }

  /** The line that stands above a question's disputes once its answer key was corrected: what the correction did. */
  protected correctionText(result: AnswerKeyCorrectionResult): string {
    if (!result.keyChanged) {
      return this.i18n.t('admin.disputes.correctionUnchanged');
    }
    const rescored = this.i18n.plural('admin.disputes.rescored', result.attemptsRescored);
    return this.i18n.t('admin.disputes.correctionChanged', { rescored });
  }

  /** The explanation or reason a settled dispute was given, labelled for who gave it. */
  protected resolutionLine(dispute: DisputeRow): string {
    const label = this.i18n.t(
      dispute.status === 'Accepted'
        ? 'admin.disputes.correctionReason'
        : 'admin.disputes.explanationGiven',
    );
    return this.i18n.t('admin.disputes.noteLine', { label, note: dispute.resolutionNote ?? '' });
  }

  protected startRejecting(dispute: DisputeRow): void {
    this.note.set('');
    this.rejectingId.set(dispute.id);
    this.focusLater(`reason:${dispute.id}`);
  }

  /** Puts the form away without rejecting, and gives focus back to the Reject button that opened it. */
  protected cancelRejecting(dispute: DisputeRow): void {
    this.rejectingId.set(null);
    this.focusLater(`reject:${dispute.id}`);
  }

  protected confirmReject(dispute: DisputeRow): void {
    const note = this.note().trim();
    if (this.busyId() !== null || note === '') {
      return;
    }

    this.busyId.set(dispute.id);
    this.rowErrors.update((errors) =>
      Object.fromEntries(Object.entries(errors).filter(([id]) => id !== dispute.id)),
    );
    this.api.rejectDispute(dispute.id, note).subscribe({
      next: () => {
        this.busyId.set(null);
        this.rejectingId.set(null);
        this.decide([dispute.id], 'Rejected');
        this.focusLater(`result:${dispute.id}`);
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.rowErrors.update((errors) => ({
          ...errors,
          [dispute.id]: extractErrorMessage(error, this.i18n.t('common.somethingWrong')),
        }));
      },
    });
  }

  protected startCorrecting(group: DisputeGroup): void {
    this.correctingQuestionId.set(group.questionId);
  }

  /** Closes the answer-key panel without a correction, and gives focus back to the button that opened it. */
  protected cancelCorrecting(): void {
    const questionId = this.correctingQuestionId();
    this.correctingQuestionId.set(null);
    if (questionId !== null) {
      this.focusLater(`correct:${questionId}`);
    }
  }

  /**
   * Shows what the correction did, in place above the question's disputes, then reads the queue again. The API does not say which
   * disputes a correction accepted, but it accepts every open one when the key changes, so those are shown as accepted here.
   */
  protected onCorrected(result: AnswerKeyCorrectionResult): void {
    const questionId = this.correctingQuestionId();
    this.correctingQuestionId.set(null);
    if (questionId === null) {
      return;
    }

    if (result.keyChanged) {
      const accepted = this.disputes()
        .filter(
          (dispute) =>
            dispute.questionId === questionId &&
            dispute.status === 'Open' &&
            this.decisions()[dispute.id] === undefined,
        )
        .map((dispute) => dispute.id);
      this.decide(accepted, 'Accepted');
    }
    this.corrections.update((current) => ({ ...current, [questionId]: result }));
    this.focusLater(`correction:${questionId}`);
    this.fetch();
  }

  /** Records a decision on each of these disputes, so each one shows its line in place of its row. */
  private decide(ids: readonly string[], status: DecidedStatus): void {
    if (ids.length === 0) {
      return;
    }
    this.decisions.update((current) => ({
      ...current,
      ...Object.fromEntries(ids.map((id): [string, DecidedStatus] => [id, status])),
    }));
  }

  /** Reads the queue for the chosen status. Disputes decided here stay where they were, even once the API stops listing them as open. */
  private fetch(): void {
    this.listing?.unsubscribe();
    this.listing = this.api.listDisputes(this.status()).subscribe({
      next: (fresh) => {
        this.disputes.set(this.keepDecided(fresh));
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /**
   * The queue as the API gives it now, with the disputes decided in this visit kept in their places. The API no longer lists an accepted
   * or rejected dispute as open, so without this the line for a decision would vanish under the person who made it.
   */
  private keepDecided(fresh: DisputeRow[]): DisputeRow[] {
    const decided = this.decisions();
    const incoming = new Map(fresh.map((dispute): [string, DisputeRow] => [dispute.id, dispute]));
    const kept: DisputeRow[] = [];
    for (const previous of this.disputes()) {
      const current = incoming.get(previous.id);
      if (current !== undefined) {
        kept.push(current);
        incoming.delete(previous.id);
      } else if (decided[previous.id] !== undefined) {
        kept.push(previous);
      }
    }
    return [...kept, ...incoming.values()];
  }

  /**
   * Moves focus to the element marked with this key once the change is drawn. The change removes or replaces the element that had
   * focus, so without this the focus would fall back to the top of the page.
   */
  private focusLater(key: string): void {
    afterNextRender(
      () =>
        this.focusTargets()
          .find((target) => target.nativeElement.getAttribute('data-focus') === key)
          ?.nativeElement.focus(),
      { injector: this.injector },
    );
  }
}
