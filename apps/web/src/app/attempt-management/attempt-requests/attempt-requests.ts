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
import { MAX_ATTEMPT_REQUEST_TEXT } from '../../candidate/candidate.models';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { AttemptAdminApiService } from '../attempt-admin-api.service';
import { AttemptRequestRow } from '../attempt-admin.models';

/** What was done with a request during this visit, and whether the candidate was e-mailed the answer. */
interface RequestDecision {
  outcome: 'given' | 'declined';
  /** False means no e-mail reached the candidate, so the administrator has to tell them another way. */
  emailed: boolean;
}

/** One entry of the queue: a request still waiting, or the decision made on it, kept in the same place so nothing jumps. */
interface QueueEntry {
  request: AttemptRequestRow;
  decision: RequestDecision | null;
}

/**
 * Admin page: candidates' requests for another attempt, oldest first, each to approve or decline (the queue behind "ask an
 * administrator"). Approving gives the attempt at once; declining can say why, and the candidate sees that. The API decides
 * whether an approval is still possible (the exam may have closed meanwhile) and this page shows its reason instead of guessing.
 *
 * A decision does not take the request off the page. Its card becomes a line saying what was done, in the same place, and focus
 * moves to that line. A confirmation at the top of a long queue would be out of sight, and the card's buttons would take the focus
 * with them when they went. The lines stay until the page is left or reloaded, so the administrator can check what they did.
 */
@Component({
  selector: 'app-attempt-requests',
  imports: [DatePipe, RouterLink, TranslatePipe],
  templateUrl: './attempt-requests.html',
  styleUrl: './attempt-requests.css',
})
export class AttemptRequests {
  private readonly api = inject(AttemptAdminApiService);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  /** The elements focus can be moved to, each marked with its `data-focus` key, so focus lands where the person was working. */
  private readonly focusTargets = viewChildren<ElementRef<HTMLElement>>('focusTarget');

  protected readonly maxNoteLength = MAX_ATTEMPT_REQUEST_TEXT;

  protected readonly entries = signal<QueueEntry[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  /** The request a call is running for, so only its buttons are locked meanwhile. */
  protected readonly busyId = signal<string | null>(null);
  /** Why the last call about a request failed, by request id, so the reason shows on that request. */
  protected readonly rowErrors = signal<Record<string, string>>({});

  /** The request whose decline form is open; one at a time, so a stray click cannot decline two. */
  protected readonly decliningId = signal<string | null>(null);
  protected readonly note = signal('');

  /** How many requests still wait for a decision. Decided ones stay on the page, so they are not counted. */
  protected readonly waiting = computed(
    () => this.entries().filter((entry) => entry.decision === null).length,
  );

  constructor() {
    this.api.listAttemptRequests('pending').subscribe({
      next: (requests) => {
        this.entries.set(requests.map((request) => ({ request, decision: null })));
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /** The count line, in words: how many requests are waiting, or that none are. */
  protected waitingText(): string {
    const count = this.waiting();
    return count === 0
      ? this.i18n.t('admin.requests.noneWaiting')
      : this.i18n.plural('admin.requests.waiting', count);
  }

  /** The heading of a request: the candidate's address, or the words for a candidate who is no longer enrolled. */
  protected candidateTitle(request: AttemptRequestRow): string {
    return request.candidateEmail ?? this.i18n.t('admin.requests.candidateGone');
  }

  /** The candidate as a sentence names them: their address, or "the candidate" when the address is not known. */
  protected candidateName(request: AttemptRequestRow): string {
    return request.candidateEmail ?? this.i18n.t('admin.requests.candidateFallback');
  }

  /** The line that stands in for a decided request: what was done, and whether the candidate was e-mailed about it. */
  protected resultText(decision: RequestDecision, request: AttemptRequestRow): string {
    const who = this.candidateName(request);
    const outcome =
      decision.outcome === 'given'
        ? this.i18n.t('admin.requests.givenResult', { who })
        : this.i18n.t('admin.requests.declinedResult', { who });
    const notice = decision.emailed
      ? this.i18n.t('admin.requests.emailed')
      : this.i18n.t('admin.requests.notEmailed');
    return `${outcome} ${notice}`;
  }

  protected approve(request: AttemptRequestRow): void {
    this.run(request, this.api.approveAttemptRequest(request.id), 'given');
  }

  protected startDeclining(request: AttemptRequestRow): void {
    this.note.set('');
    this.decliningId.set(request.id);
    this.focusLater(`reason:${request.id}`);
  }

  /** Puts the form away without sending anything, and gives focus back to the Decline button that opened it. */
  protected cancelDeclining(request: AttemptRequestRow): void {
    this.decliningId.set(null);
    this.focusLater(`decline:${request.id}`);
  }

  protected confirmDecline(request: AttemptRequestRow): void {
    this.run(
      request,
      this.api.declineAttemptRequest(request.id, this.note().trim() || null),
      'declined',
    );
  }

  private run(
    request: AttemptRequestRow,
    call: ReturnType<AttemptAdminApiService['approveAttemptRequest']>,
    outcome: RequestDecision['outcome'],
  ): void {
    if (this.busyId() !== null) {
      return;
    }

    this.busyId.set(request.id);
    this.rowErrors.update((errors) =>
      Object.fromEntries(Object.entries(errors).filter(([id]) => id !== request.id)),
    );
    call.subscribe({
      next: (answer) => {
        this.busyId.set(null);
        this.decliningId.set(null);
        // The request keeps its place; only its card becomes the line that says what was decided.
        const decision: RequestDecision = { outcome, emailed: answer.candidateNotified === true };
        this.entries.update((list) =>
          list.map((entry) => (entry.request.id === request.id ? { ...entry, decision } : entry)),
        );
        this.focusLater(`result:${request.id}`);
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.rowErrors.update((errors) => ({
          ...errors,
          [request.id]: extractErrorMessage(error, this.i18n.t('common.somethingWrong')),
        }));
      },
    });
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
