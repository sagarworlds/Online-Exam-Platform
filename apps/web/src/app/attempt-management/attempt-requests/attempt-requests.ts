import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MAX_ATTEMPT_REQUEST_TEXT } from '../../candidate/candidate.models';
import { extractErrorMessage } from '../../shared/problem-details';
import { AttemptAdminApiService } from '../attempt-admin-api.service';
import { AttemptRequestRow } from '../attempt-admin.models';

/**
 * Admin page: candidates' requests for another attempt, oldest first, each to approve or decline (the queue behind "ask an
 * administrator"). Approving gives the attempt at once; declining can say why, and the candidate sees that. The API decides
 * whether an approval is still possible (the exam may have closed meanwhile) and this page shows its reason instead of guessing.
 */
@Component({
  selector: 'app-attempt-requests',
  imports: [DatePipe, RouterLink],
  templateUrl: './attempt-requests.html',
})
export class AttemptRequests {
  private readonly api = inject(AttemptAdminApiService);

  protected readonly requests = signal<AttemptRequestRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  /** The request a call is running for, so only its buttons are locked meanwhile. */
  protected readonly busyId = signal<string | null>(null);
  /** Why the last call about a request failed, by request id, so the reason shows on that request. */
  protected readonly rowErrors = signal<Record<string, string>>({});

  /** The request whose decline form is open; one at a time, so a stray click cannot decline two. */
  protected readonly decliningId = signal<string | null>(null);
  protected readonly note = signal('');
  protected readonly maxNoteLength = MAX_ATTEMPT_REQUEST_TEXT;

  constructor() {
    this.api.listAttemptRequests('pending').subscribe({
      next: (requests) => {
        this.requests.set(requests);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  protected approve(request: AttemptRequestRow): void {
    this.run(request, this.api.approveAttemptRequest(request.id), `Gave ${request.candidateEmail ?? 'the candidate'} another attempt.`);
  }

  protected startDeclining(request: AttemptRequestRow): void {
    this.note.set('');
    this.decliningId.set(request.id);
  }

  protected cancelDeclining(): void {
    this.decliningId.set(null);
  }

  protected confirmDecline(request: AttemptRequestRow): void {
    this.run(
      request,
      this.api.declineAttemptRequest(request.id, this.note().trim() || null),
      `Declined the request from ${request.candidateEmail ?? 'the candidate'}.`,
    );
  }

  private run(request: AttemptRequestRow, call: ReturnType<AttemptAdminApiService['approveAttemptRequest']>, done: string): void {
    if (this.busyId() !== null) {
      return;
    }

    this.busyId.set(request.id);
    this.notice.set(null);
    this.rowErrors.update((errors) => Object.fromEntries(Object.entries(errors).filter(([id]) => id !== request.id)));
    call.subscribe({
      next: () => {
        this.busyId.set(null);
        this.decliningId.set(null);
        // Decided, so it leaves the waiting queue.
        this.requests.update((list) => list.filter((r) => r.id !== request.id));
        this.notice.set(done);
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.rowErrors.update((errors) => ({ ...errors, [request.id]: extractErrorMessage(error) }));
      },
    });
  }
}
