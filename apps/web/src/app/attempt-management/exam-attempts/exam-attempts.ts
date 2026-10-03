import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../shared/problem-details';
import { AttemptAdminApiService } from '../attempt-admin-api.service';
import { AttemptPaperDto, ExamAttemptsDto, ExamCandidateDto } from '../attempt-admin.models';
import { PlainTextPipe } from '../../shared/rich-text/plain-text.pipe';

/** The longest reason the API accepts. */
const MAX_REASON_LENGTH = 500;

/**
 * Admin page for one exam's candidates: who has taken it, each attempt's result, and a way to give a candidate another attempt
 * when they ask for one (a power cut, a dropped connection). The API decides whether another can be given, and this page
 * says why when it cannot; it never offers what the API would refuse.
 */
@Component({
  selector: 'app-exam-attempts',
  imports: [RouterLink, DatePipe, PlainTextPipe],
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
