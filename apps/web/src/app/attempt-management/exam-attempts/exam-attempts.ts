import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { extractErrorMessage } from '../../shared/problem-details';
import { AttemptAdminApiService } from '../attempt-admin-api.service';
import { AttemptSummaryDto } from '../../candidate/candidate.models';
import { AttemptClientDto, AttemptPaperDto, ExamAttemptsDto, ExamCandidateDto } from '../attempt-admin.models';
import { MathDirective } from '../../shared/rich-text/math.directive';
import { AnswerVerdict } from '../../candidate/candidate.models';

/** The longest reason the API accepts. */
const MAX_REASON_LENGTH = 500;

/** The actions that need words from the administrator: a warning's text, or the reason shown to the candidate. */
type ActionKind = 'warn' | 'terminate' | 'invalidate';

/** What each wording action asks, and what its button says. */
const ACTIONS: Record<ActionKind, { label: string; hint: string; button: string; failure: string }> = {
  warn: {
    label: 'Warning to show the candidate',
    hint: 'Shown on their exam page within a few seconds, and kept on record.',
    button: 'Send warning',
    failure: 'The warning could not be sent. Please try again.',
  },
  terminate: {
    label: 'Why is this attempt being ended?',
    hint: 'The candidate is shown this. The attempt is scored with the answers they had saved, and cannot be reopened.',
    button: 'End attempt',
    failure: 'The attempt could not be ended. Please try again.',
  },
  invalidate: {
    label: 'Why is this result being invalidated?',
    hint: 'The candidate is shown this instead of a score, and can no longer review their answers. The score stays on record for you.',
    button: 'Invalidate result',
    failure: 'The result could not be invalidated. Please try again.',
  },
};

/**
 * Admin page for one exam's candidates: who has taken it, each attempt's result, and a way to give a candidate another attempt
 * when they ask for one (a power cut, a dropped connection). The API decides whether another can be given, and this page
 * says why when it cannot; it never offers what the API would refuse.
 */
@Component({
  selector: 'app-exam-attempts',
  imports: [RouterLink, DatePipe, MathDirective],
  templateUrl: './exam-attempts.html',
})
export class ExamAttempts {
  private readonly api = inject(AttemptAdminApiService);
  protected readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  protected readonly maxReasonLength = MAX_REASON_LENGTH;

  protected readonly data = signal<ExamAttemptsDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly busy = signal(false);

  /** The candidate whose "give another attempt" form is open; only one at a time, so a stray click cannot grant two. */
  protected readonly grantingFor = signal<string | null>(null);
  protected readonly reason = signal('');

  /** The attempt whose paper is open, and what it holds once loaded; one at a time keeps the page short. */
  protected readonly paperFor = signal<string | null>(null);
  protected readonly paper = signal<AttemptPaperDto | null>(null);

  protected optionLetter(index: number): string {
    return String.fromCharCode(65 + index);
  }

  /** `+4`, `0` or `-1`: the sign is always shown so a gain and a loss cannot be mistaken for each other. */
  protected formatMarks(marks: number): string {
    return marks > 0 ? `+${marks}` : `${marks}`;
  }

  protected verdictLabel(verdict: AnswerVerdict): string {
    return verdict === 'Partial' ? 'Partly correct' : verdict === 'Unanswered' ? 'Not answered' : verdict;
  }

  /** How many questions of the paper were marked with each verdict, for the line above it. */
  protected verdictCount(paper: AttemptPaperDto, verdict: AnswerVerdict): number {
    return paper.sections.reduce((n, s) => n + s.questions.filter((q) => q.verdict === verdict).length, 0);
  }

  /** The attempt whose sign-in details (where it was sat from, FR-26) are open, and what they hold once loaded. */
  protected readonly clientsFor = signal<string | null>(null);
  protected readonly clients = signal<AttemptClientDto[] | null>(null);

  /** The attempt a wording action (warn, end, invalidate) is open for; only one at a time, so a stray click cannot act on two. */
  protected readonly acting = signal<{ attemptId: string; kind: ActionKind } | null>(null);
  protected readonly actionText = signal('');
  protected readonly actions = ACTIONS;

  constructor() {
    this.api.getExamAttempts(this.examId).subscribe({
      next: (data) => {
        this.data.set(data);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  protected togglePaper(attemptId: string): void {
    if (this.paperFor() === attemptId) {
      this.paperFor.set(null);
      this.paper.set(null);
      return;
    }

    this.errorMessage.set(null);
    this.paperFor.set(attemptId);
    this.paper.set(null);
    this.api.getAttemptPaper(this.examId, attemptId).subscribe({
      next: (paper) => {
        // A reply for a paper staff has since closed, or replaced by another attempt's, is ignored.
        if (this.paperFor() === attemptId) {
          this.paper.set(paper);
        }
      },
      error: (error: unknown) => {
        this.paperFor.set(null);
        this.errorMessage.set(extractErrorMessage(error, 'The paper could not be loaded. Please try again.'));
      },
    });
  }

  protected toggleClients(attemptId: string): void {
    if (this.clientsFor() === attemptId) {
      this.clientsFor.set(null);
      this.clients.set(null);
      return;
    }

    this.errorMessage.set(null);
    this.clientsFor.set(attemptId);
    this.clients.set(null);
    this.api.getAttemptClients(this.examId, attemptId).subscribe({
      next: (rows) => {
        // A reply for details staff has since closed, or replaced by another attempt's, is ignored.
        if (this.clientsFor() === attemptId) {
          this.clients.set(rows);
        }
      },
      error: (error: unknown) => {
        this.clientsFor.set(null);
        this.errorMessage.set(extractErrorMessage(error, 'The sign-in details could not be loaded. Please try again.'));
      },
    });
  }

  /** The first characters of a device signature, enough to tell two apart on screen; the full value is a hash nobody reads. */
  protected shortDevice(signature: string | null): string {
    return signature === null ? 'not sent' : signature.slice(0, 8);
  }

  protected beginAction(attemptId: string, kind: ActionKind): void {
    this.notice.set(null);
    this.errorMessage.set(null);
    this.actionText.set('');
    this.acting.set({ attemptId, kind });
  }

  protected cancelAction(): void {
    this.acting.set(null);
  }

  protected setActionText(value: string): void {
    this.actionText.set(value);
  }

  /** Sends the open wording action. Nothing is sent without words: the API would refuse, and the candidate is shown them. */
  protected confirmAction(candidate: ExamCandidateDto, attempt: AttemptSummaryDto): void {
    const acting = this.acting();
    const text = this.actionText().trim();
    if (acting === null || acting.attemptId !== attempt.id || this.busy() || text === '') {
      return;
    }

    const call =
      acting.kind === 'warn'
        ? this.api.warnAttempt(this.examId, attempt.id, text)
        : acting.kind === 'terminate'
          ? this.api.terminateAttempt(this.examId, attempt.id, text)
          : this.api.invalidateAttempt(this.examId, attempt.id, text);
    const done = {
      warn: `Warning sent to ${candidate.email}.`,
      terminate: `Attempt ${attempt.number} of ${candidate.email} was ended.`,
      invalidate: `The result of attempt ${attempt.number} of ${candidate.email} was invalidated.`,
    }[acting.kind];
    this.run(call, done, ACTIONS[acting.kind].failure, () => this.acting.set(null));
  }

  protected pause(candidate: ExamCandidateDto, attempt: AttemptSummaryDto): void {
    this.run(this.api.pauseAttempt(this.examId, attempt.id), `Attempt ${attempt.number} of ${candidate.email} is paused.`, 'The attempt could not be paused. Please try again.');
  }

  protected resume(candidate: ExamCandidateDto, attempt: AttemptSummaryDto): void {
    this.run(this.api.resumeAttempt(this.examId, attempt.id), `Attempt ${attempt.number} of ${candidate.email} was resumed; it has the time it had left.`, 'The attempt could not be resumed. Please try again.');
  }

  /** Runs one action and replaces the attempt's row with what the API answers, which is the truth about it now. */
  private run(call: Observable<AttemptSummaryDto>, done: string, failure: string, onDone: () => void = () => undefined): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.errorMessage.set(null);
    call.subscribe({
      next: (updated) => {
        this.data.update((data) =>
          data === null
            ? data
            : { ...data, candidates: data.candidates.map((c) => ({ ...c, attempts: c.attempts.map((a) => (a.id === updated.id ? updated : a)) })) },
        );
        this.notice.set(done);
        onDone();
        this.busy.set(false);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.errorMessage.set(extractErrorMessage(error, failure));
      },
    });
  }

  protected startGranting(candidate: ExamCandidateDto): void {
    this.notice.set(null);
    this.errorMessage.set(null);
    this.reason.set('');
    this.grantingFor.set(candidate.candidateId);
  }

  protected cancelGranting(): void {
    this.grantingFor.set(null);
  }

  protected confirmGrant(candidate: ExamCandidateDto): void {
    if (this.busy() || !candidate.canGrant) {
      return;
    }

    this.busy.set(true);
    this.errorMessage.set(null);
    const reason = this.reason().trim();
    this.api.grantExtraAttempt(this.examId, candidate.candidateId, reason === '' ? null : reason).subscribe({
      next: (row) => {
        // What the API answers is the truth about this candidate now, so the row is replaced by it rather than patched.
        this.data.update((data) => (data === null ? data : { ...data, candidates: data.candidates.map((c) => (c.candidateId === row.candidateId ? row : c)) }));
        this.notice.set(`${row.email} can now make attempt ${row.attemptsAllowed}.`);
        this.grantingFor.set(null);
        this.busy.set(false);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.errorMessage.set(extractErrorMessage(error, 'The attempt could not be given. Please try again.'));
      },
    });
  }

  /** Why another attempt cannot be given to this candidate right now, in words; null when it can. */
  protected whyNot(candidate: ExamCandidateDto): string | null {
    if (candidate.canGrant) {
      return null;
    }
    if (this.data()?.windowClosed) {
      return 'The exam can no longer be started.';
    }
    if (candidate.attemptsUsed > candidate.attemptsAllowed) {
      // Only a lowered limit gets here: one more would still leave them over it, so the way out is the exam's limit.
      return "Has made more attempts than the exam now allows. Raise the exam's attempts allowed to let them sit it again.";
    }
    return candidate.attemptsUsed < candidate.attemptsAllowed ? 'Still has an attempt left to use.' : null;
  }

  protected setReason(value: string): void {
    this.reason.set(value);
  }
}
