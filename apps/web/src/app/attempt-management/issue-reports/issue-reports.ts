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
import { IssueCategory } from '../../candidate/candidate.models';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { MathDirective } from '../../shared/rich-text/math.directive';
import { AttemptAdminApiService } from '../attempt-admin-api.service';
import {
  IssueReportFilterStatus,
  IssueReportRow,
  IssueReportStatus,
  MAX_ISSUE_RESOLUTION_NOTE,
} from '../attempt-admin.models';

/** A report resolved during this visit, with the note written with it (null when none was). It stands in the report's place until the page is left. */
interface ResolutionDecision {
  note: string | null;
}

const CATEGORY_KEYS: Readonly<Record<IssueCategory, MessageKey>> = {
  Question: 'admin.issues.category.Question',
  Technical: 'admin.issues.category.Technical',
  Other: 'admin.issues.category.Other',
};

const STATUS_KEYS: Readonly<Record<IssueReportStatus, MessageKey>> = {
  Open: 'admin.issues.status.Open',
  Resolved: 'admin.issues.status.Resolved',
};

const EMPTY_KEYS: Readonly<Record<IssueReportFilterStatus, MessageKey>> = {
  open: 'admin.issues.noneOpen',
  resolved: 'admin.issues.noneResolved',
};

/**
 * Admin page: the problems candidates reported from inside an exam (FR-42), oldest first. A report only tells staff; it changes nothing
 * about the attempt, which went on. Staff read it, deal with it however it needs (fix a question, look into a connection, pause or
 * invalidate an attempt from the exam's candidates page) and mark it resolved, optionally saying what they did. The API decides whether
 * a report can still be resolved and this page shows its reason instead of guessing.
 *
 * Resolving does not take the report off the page. Its card becomes a line saying so, in the same place, and focus moves to that line,
 * so the next report is not lost below the fold. The lines stay until the page is left or reloaded.
 */
@Component({
  selector: 'app-issue-reports',
  imports: [DatePipe, RouterLink, MathDirective, TranslatePipe],
  templateUrl: './issue-reports.html',
  styleUrl: './issue-reports.css',
})
export class IssueReports {
  private readonly api = inject(AttemptAdminApiService);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  /** The elements focus can be moved to, each marked with its `data-focus` key, so focus lands where the person was working. */
  private readonly focusTargets = viewChildren<ElementRef<HTMLElement>>('focusTarget');
  /** The read of the queue in flight, so a newer read (another status chosen meanwhile) is never overwritten by an older one. */
  private listing: Subscription | null = null;

  protected readonly reports = signal<IssueReportRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly status = signal<IssueReportFilterStatus>('open');
  protected readonly statuses: readonly { value: IssueReportFilterStatus; label: MessageKey }[] = [
    { value: 'open', label: 'admin.issues.filter.open' },
    { value: 'resolved', label: 'admin.issues.filter.resolved' },
  ];

  /** The resolutions made in this visit, by report id; each stands in its report's place until the page is left. */
  protected readonly resolutions = signal<Readonly<Record<string, ResolutionDecision>>>({});

  /** The report a call is running for, so only its buttons are locked meanwhile. */
  protected readonly busyId = signal<string | null>(null);
  /** Why the last call about a report failed, by report id, so the reason shows on that report. */
  protected readonly rowErrors = signal<Record<string, string>>({});

  /** The report whose resolve form is open; one at a time, so a stray click cannot resolve two. */
  protected readonly resolvingId = signal<string | null>(null);
  protected readonly note = signal('');
  protected readonly maxNoteLength = MAX_ISSUE_RESOLUTION_NOTE;

  /** How many reports still wait: open on the server and not resolved here. */
  protected readonly waiting = computed(
    () =>
      this.reports().filter((r) => r.status === 'Open' && this.resolutions()[r.id] === undefined)
        .length,
  );

  constructor() {
    this.fetch();
  }

  protected onStatusChanged(value: string): void {
    const status = this.statuses.find((s) => s.value === value)?.value;
    if (status === undefined || status === this.status()) {
      return;
    }

    // Resolutions belong to the list they were made on, so a new list starts clean.
    this.status.set(status);
    this.resolvingId.set(null);
    this.resolutions.set({});
    this.loading.set(true);
    this.errorMessage.set(null);
    this.reports.set([]);
    this.fetch();
  }

  /** The count line, in words: how many reports are waiting, or that none are. */
  protected waitingText(): string {
    const count = this.waiting();
    return count === 0
      ? this.i18n.t('admin.issues.noneWaiting')
      : this.i18n.plural('admin.issues.waiting', count);
  }

  protected emptyMessage(): string {
    return this.i18n.t(EMPTY_KEYS[this.status()]);
  }

  protected statusLabel(status: IssueReportStatus): string {
    return this.i18n.t(STATUS_KEYS[status]);
  }

  /** What a kind of problem is called on the page. */
  protected categoryLabel(category: IssueCategory): string {
    return this.i18n.t(CATEGORY_KEYS[category]);
  }

  /** The candidate's address, or "the candidate" in a sentence when it is not known. */
  protected candidateName(report: IssueReportRow): string {
    return report.candidateEmail ?? this.i18n.t('admin.issues.candidateFallback');
  }

  /** The line that stands for a report resolved here: that it was marked resolved. */
  protected resolvedText(report: IssueReportRow): string {
    return this.i18n.t('admin.issues.resolvedResult', { who: this.candidateName(report) });
  }

  protected startResolving(report: IssueReportRow): void {
    this.note.set('');
    this.resolvingId.set(report.id);
    this.focusLater(`note:${report.id}`);
  }

  /** Puts the form away without resolving anything, and gives focus back to the Resolve button that opened it. */
  protected cancelResolving(report: IssueReportRow): void {
    this.resolvingId.set(null);
    this.focusLater(`resolve:${report.id}`);
  }

  protected confirmResolve(report: IssueReportRow): void {
    if (this.busyId() !== null) {
      return;
    }

    const note = this.note().trim() === '' ? null : this.note().trim();
    this.busyId.set(report.id);
    this.rowErrors.update((errors) =>
      Object.fromEntries(Object.entries(errors).filter(([id]) => id !== report.id)),
    );
    this.api.resolveIssueReport(report.id, this.note()).subscribe({
      next: () => {
        this.busyId.set(null);
        this.resolvingId.set(null);
        // The report keeps its place; only its card becomes the line that says it was resolved.
        this.resolutions.update((current) => ({ ...current, [report.id]: { note } }));
        this.focusLater(`result:${report.id}`);
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.rowErrors.update((errors) => ({
          ...errors,
          [report.id]: extractErrorMessage(error, this.i18n.t('common.somethingWrong')),
        }));
      },
    });
  }

  /** Reads the queue for the chosen status. Resolutions made here are not re-read: they keep their place until the page is left. */
  private fetch(): void {
    this.listing?.unsubscribe();
    this.listing = this.api.listIssueReports(this.status()).subscribe({
      next: (reports) => {
        this.reports.set(reports);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /**
   * Moves focus to the element marked with this key once the change is drawn. The change removes or replaces the element that had focus,
   * so without this the focus would fall back to the top of the page.
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
