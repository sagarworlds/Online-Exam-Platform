import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { IssueCategory } from '../../candidate/candidate.models';
import { extractErrorMessage } from '../../shared/problem-details';
import { MathDirective } from '../../shared/rich-text/math.directive';
import { AttemptAdminApiService } from '../attempt-admin-api.service';
import { IssueReportFilterStatus, IssueReportRow, MAX_ISSUE_RESOLUTION_NOTE } from '../attempt-admin.models';

/**
 * Admin page: the problems candidates reported from inside an exam (FR-42), oldest first. A report only tells staff; it changes nothing
 * about the attempt, which went on. Staff read it, deal with it however it needs (fix a question, look into a connection, pause or
 * invalidate an attempt from the exam's candidates page) and mark it resolved, optionally saying what they did. The API decides whether
 * a report can still be resolved and this page shows its reason instead of guessing.
 */
@Component({
  selector: 'app-issue-reports',
  imports: [DatePipe, RouterLink, MathDirective],
  templateUrl: './issue-reports.html',
})
export class IssueReports {
  private readonly api = inject(AttemptAdminApiService);
  /** The read of the queue in flight, so a newer read (another status chosen meanwhile) is never overwritten by an older one. */
  private listing: Subscription | null = null;

  protected readonly reports = signal<IssueReportRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly status = signal<IssueReportFilterStatus>('open');
  protected readonly statuses: readonly { value: IssueReportFilterStatus; label: string }[] = [
    { value: 'open', label: 'Open' },
    { value: 'resolved', label: 'Resolved' },
  ];

  /** The report a call is running for, so only its buttons are locked meanwhile. */
  protected readonly busyId = signal<string | null>(null);
  /** Why the last call about a report failed, by report id, so the reason shows on that report. */
  protected readonly rowErrors = signal<Record<string, string>>({});

  /** The report whose resolve form is open; one at a time, so a stray click cannot resolve two. */
  protected readonly resolvingId = signal<string | null>(null);
  protected readonly note = signal('');
  protected readonly maxNoteLength = MAX_ISSUE_RESOLUTION_NOTE;

  constructor() {
    this.fetch();
  }

  protected onStatusChanged(value: string): void {
    const status = this.statuses.find((s) => s.value === value)?.value;
    if (status === undefined || status === this.status()) {
      return;
    }

    this.status.set(status);
    this.resolvingId.set(null);
    this.notice.set(null);
    this.loading.set(true);
    this.errorMessage.set(null);
    this.reports.set([]);
    this.fetch();
  }

  protected emptyMessage(): string {
    return `No ${this.status()} reports.`;
  }

  /** What a kind of problem is called on the page. */
  protected categoryLabel(category: IssueCategory): string {
    switch (category) {
      case 'Question':
        return 'Question';
      case 'Technical':
        return 'Technical';
      default:
        return 'Other';
    }
  }

  protected startResolving(report: IssueReportRow): void {
    this.note.set('');
    this.resolvingId.set(report.id);
  }

  protected cancelResolving(): void {
    this.resolvingId.set(null);
  }

  protected confirmResolve(report: IssueReportRow): void {
    if (this.busyId() !== null) {
      return;
    }

    this.busyId.set(report.id);
    this.notice.set(null);
    this.rowErrors.update((errors) => Object.fromEntries(Object.entries(errors).filter(([id]) => id !== report.id)));
    this.api.resolveIssueReport(report.id, this.note()).subscribe({
      next: () => {
        this.busyId.set(null);
        this.resolvingId.set(null);
        this.notice.set(`Marked the report from ${report.candidateEmail ?? 'the candidate'} as resolved.`);
        this.fetch();
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.rowErrors.update((errors) => ({ ...errors, [report.id]: extractErrorMessage(error) }));
      },
    });
  }

  /** Reads the queue for the chosen status. After an action the list stays up meanwhile, so the page does not jump. */
  private fetch(): void {
    this.listing?.unsubscribe();
    this.listing = this.api.listIssueReports(this.status()).subscribe({
      next: (reports) => {
        this.reports.set(reports);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
