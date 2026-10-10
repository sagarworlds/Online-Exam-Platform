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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MessageKey } from '../../i18n/messages.en';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { ProctoringApiService } from '../proctoring-api.service';
import {
  MAX_RISK_NOTE_LENGTH,
  RISK_SIGNAL_NAMES,
  RiskFlag,
  RiskFlagFilterName,
  RiskScanResult,
  RiskSignal,
} from '../proctoring.models';

/** What a reviewer did to a flag during this visit, so its card can become the line that says so. */
interface RecordedDecision {
  outcome: 'reviewed' | 'dismissed';
  /** The note given with a dismissal; null for a review, which needs none. */
  note: string | null;
}

/** One card of the queue: a flag still on the page, and the decision made on it during this visit, if any. */
interface QueueEntry {
  flag: RiskFlag;
  decision: RecordedDecision | null;
}

/** The views offered above the queue, in the order they are offered. */
const FILTERS: readonly { value: RiskFlagFilterName; label: MessageKey }[] = [
  { value: 'open', label: 'proctoring.filter.open' },
  { value: 'reviewed', label: 'proctoring.filter.reviewed' },
  { value: 'dismissed', label: 'proctoring.filter.dismissed' },
  { value: 'all', label: 'proctoring.filter.all' },
];

/**
 * Staff page: the review queue of one exam (FR-27). Flagged attempts come highest score first, and each card shows every signal
 * behind its score, so a reviewer can check the sum rather than trust a bare number. A reviewer marks a flag reviewed, or dismisses it
 * with a note saying why. Nothing on this page changes the candidate's attempt, result, access or messages.
 *
 * Like the attempt-requests queue, a decision does not remove its card: the card becomes a line saying what was decided, in the same
 * place, and focus moves to that line, so the reviewer can see what they did. The lines stay until the page is left or reloaded.
 */
@Component({
  selector: 'app-risk-flag-queue',
  imports: [DatePipe, RouterLink, TranslatePipe],
  templateUrl: './risk-flag-queue.html',
  styleUrl: './risk-flag-queue.css',
})
export class RiskFlagQueue {
  private readonly api = inject(ProctoringApiService);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  /** The elements focus can be moved to, each marked with its `data-focus` key, so focus lands where the reviewer was working. */
  private readonly focusTargets = viewChildren<ElementRef<HTMLElement>>('focusTarget');

  protected readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  protected readonly filters = FILTERS;
  protected readonly maxNoteLength = MAX_RISK_NOTE_LENGTH;

  protected readonly examName = signal<string | null>(null);
  protected readonly filter = signal<RiskFlagFilterName>('open');
  protected readonly entries = signal<QueueEntry[]>([]);
  protected readonly total = signal(0);
  /** Whether a queue has been read, so the under-18 notice is shown only once the server has said what the gate is. */
  protected readonly loaded = signal(false);
  /** Whether attempts by candidates under 18 may be scored here, as the server says. */
  protected readonly minorsScanEnabled = signal(false);
  /** How many finished attempts were left out of scoring because the candidate was under 18. */
  protected readonly excludedUnder18 = signal(0);
  protected readonly page = signal(1);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly scanning = signal(false);
  protected readonly scanResult = signal<RiskScanResult | null>(null);
  protected readonly scanError = signal<string | null>(null);

  /** The flag a call is running for, so only its buttons are locked meanwhile. */
  protected readonly busyId = signal<string | null>(null);
  /** Why the last call about a flag failed, by flag id, so the reason shows on that flag. */
  protected readonly rowErrors = signal<Record<string, string>>({});

  /** The flag whose dismissal form is open; one at a time, so a stray click cannot dismiss two. */
  protected readonly dismissingId = signal<string | null>(null);
  protected readonly note = signal('');
  /** Set when a dismissal is pressed without a note, so the reason shows next to the note box. */
  protected readonly noteError = signal<string | null>(null);

  /** Whether the server has more flags than the page shows. */
  protected readonly hasMore = computed(() => this.entries().length < this.total());

  constructor() {
    this.request(1, false);
  }

  /** Switches to another view of the queue and reads it from the first page. */
  protected setFilter(value: RiskFlagFilterName): void {
    if (value === this.filter()) {
      return;
    }
    this.filter.set(value);
    this.request(1, false);
  }

  /** Reads the next page of the same view and adds it below the cards already shown. */
  protected showMore(): void {
    this.request(this.page() + 1, true);
  }

  /** Tries the read again after it failed. */
  protected retry(): void {
    this.request(this.page(), this.entries().length > 0);
  }

  /** Asks the server to score the exam's finished attempts, then reads the queue again. */
  protected scan(): void {
    if (this.scanning()) {
      return;
    }
    this.scanning.set(true);
    this.scanResult.set(null);
    this.scanError.set(null);
    this.api.runRiskScan(this.examId).subscribe({
      next: (result) => {
        this.scanning.set(false);
        this.scanResult.set(result);
        this.request(1, false);
      },
      error: (error: unknown) => {
        this.scanning.set(false);
        this.scanError.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  protected review(flag: RiskFlag): void {
    this.run(flag, this.api.reviewRiskFlag(flag.id, null), { outcome: 'reviewed', note: null });
  }

  protected startDismissing(flag: RiskFlag): void {
    this.note.set('');
    this.noteError.set(null);
    this.dismissingId.set(flag.id);
    this.focusLater(`note:${flag.id}`);
  }

  /** Puts the dismissal form away without sending anything, and gives focus back to the button that opened it. */
  protected cancelDismissing(flag: RiskFlag): void {
    this.dismissingId.set(null);
    this.noteError.set(null);
    this.focusLater(`dismiss:${flag.id}`);
  }

  protected confirmDismiss(flag: RiskFlag): void {
    const note = this.note().trim();
    if (note === '') {
      // The API would refuse this too; the page says so here, in words, rather than sending a request that comes back as an error.
      this.noteError.set(this.i18n.t('proctoring.dismiss.required'));
      this.focusLater(`note:${flag.id}`);
      return;
    }
    this.noteError.set(null);
    this.run(flag, this.api.dismissRiskFlag(flag.id, note), { outcome: 'dismissed', note });
  }

  /** The count line: how many flags the view holds, in words. */
  protected countText(): string {
    return this.i18n.plural('proctoring.count', this.total());
  }

  /** The notice for finished attempts left out because the candidate is under 18, with the count in words. */
  protected gateOffText(): string {
    return this.i18n.plural('proctoring.gate.off', this.excludedUnder18());
  }

  /** The sentence after a scan that left attempts out, with the count in words. */
  protected scanExcludedText(count: number): string {
    return this.i18n.plural('proctoring.scan.excluded', count);
  }

  /** The words shown when the view holds no flag. */
  protected emptyText(): string {
    return this.filter() === 'open' ? this.i18n.t('proctoring.none.open') : this.i18n.t('proctoring.none.other');
  }

  /** The heading of a card: the candidate's address, or the words for a candidate no longer on the roster. */
  protected candidateTitle(flag: RiskFlag): string {
    return flag.candidateEmail ?? this.i18n.t('proctoring.card.noEmail');
  }

  /** The candidate as a sentence names them. */
  protected candidateName(flag: RiskFlag): string {
    return flag.candidateEmail ?? this.i18n.t('proctoring.candidateFallback');
  }

  /** Whether a reviewer may still decide on the flag. A flag decided earlier is shown with what was decided, and no buttons. */
  protected isOpen(flag: RiskFlag): boolean {
    return flag.status === 'Open';
  }

  /** The name of a signal, or the name the API gave it when this page does not know it. */
  protected signalName(signal: RiskSignal): string {
    const key = RISK_SIGNAL_NAMES[signal.kind];
    return key ? this.i18n.t(key) : signal.kind;
  }

  /** The value read, in words; a signal that could not be judged says so rather than showing a number. */
  protected valueText(signal: RiskSignal): string {
    return signal.value === null
      ? this.i18n.t('proctoring.signal.notJudged')
      : this.i18n.t('proctoring.signal.value', { value: formatNumber(signal.value) });
  }

  /** The rule the value was judged by, in words. */
  protected ruleText(signal: RiskSignal): string {
    const key = signal.raisedWhen === 'AtMost' ? 'proctoring.signal.ruleAtMost' : 'proctoring.signal.ruleAtLeast';
    return this.i18n.t(key, { threshold: formatNumber(signal.threshold) });
  }

  protected raisedText(signal: RiskSignal): string {
    return this.i18n.t(signal.raised ? 'proctoring.signal.raised' : 'proctoring.signal.notRaised');
  }

  protected pointsText(signal: RiskSignal): string {
    return this.i18n.t('proctoring.signal.points', { points: signal.points, weight: signal.weight });
  }

  /** The line that stands in for a flag decided during this visit. */
  protected resultText(decision: RecordedDecision): string {
    return decision.outcome === 'reviewed'
      ? this.i18n.t('proctoring.result.reviewed')
      : this.i18n.t('proctoring.result.dismissed');
  }

  /** Reads one page of a view. An appended page keeps the cards already shown and any decisions made on them. */
  private request(page: number, append: boolean): void {
    this.loading.set(!append);
    this.loadingMore.set(append);
    this.errorMessage.set(null);
    this.api.listRiskFlags(this.examId, this.filter(), page).subscribe({
      next: (queue) => {
        this.examName.set(queue.examName);
        this.total.set(queue.total);
        this.minorsScanEnabled.set(queue.minorsScanEnabled);
        this.excludedUnder18.set(queue.excludedUnder18Attempts);
        this.loaded.set(true);
        this.page.set(page);
        const fresh: QueueEntry[] = queue.items.map((flag) => ({ flag, decision: null }));
        this.entries.update((list) => (append ? [...list, ...fresh] : fresh));
        this.loading.set(false);
        this.loadingMore.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadingMore.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }

  /** Sends one decision for a flag and turns its card into the line that says what was decided. */
  private run(flag: RiskFlag, call: ReturnType<ProctoringApiService['reviewRiskFlag']>, decision: RecordedDecision): void {
    if (this.busyId() !== null) {
      return;
    }

    this.busyId.set(flag.id);
    this.rowErrors.update((errors) => Object.fromEntries(Object.entries(errors).filter(([id]) => id !== flag.id)));
    call.subscribe({
      next: () => {
        this.busyId.set(null);
        this.dismissingId.set(null);
        this.entries.update((list) =>
          list.map((entry) => (entry.flag.id === flag.id ? { ...entry, decision } : entry)),
        );
        this.focusLater(`result:${flag.id}`);
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.rowErrors.update((errors) => ({
          ...errors,
          [flag.id]: extractErrorMessage(error, this.i18n.t('common.somethingWrong')),
        }));
      },
    });
  }

  /**
   * Moves focus to the element marked with this key once the change is drawn. The change removes or replaces the element that had
   * focus, so without this focus would fall back to the top of the page.
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

/** A whole number is shown without decimals; a fraction to one place, as a pace in seconds is. */
function formatNumber(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1);
}
