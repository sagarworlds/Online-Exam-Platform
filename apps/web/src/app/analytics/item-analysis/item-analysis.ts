import { DOCUMENT } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type { MessageKey } from '../../i18n/messages.en';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractBlobErrorMessage, extractErrorMessage } from '../../shared/problem-details';
import { RefreshButton } from '../../shared/refresh-button/refresh-button';
import { AnalyticsApiService } from '../analytics-api.service';
import { ExamItemAnalysisDto, ItemRowDto, ScoreBandDto } from '../analytics.models';
import { ReportFormat, saveDownload } from '../report-download';
import { ItemSortKey, SortDirection, sortItemRows } from './item-analysis-sort';

/** What the page shows at any moment: being read, the questions, the results held, nothing to show, or a failed read. */
export type ItemAnalysisView = 'loading' | 'ready' | 'held' | 'empty' | 'error';

/** The columns the reader can sort by, in the order the sort buttons show them. */
export const ITEM_SORT_KEYS: readonly ItemSortKey[] = ['position', 'attempts', 'difficulty', 'discrimination', 'time', 'dropOff'];

/** The message that names each sort column; typed so a key missing from the messages does not compile. */
const SORT_LABEL_KEYS = {
  position: 'admin.items.sort.position',
  attempts: 'admin.items.sort.attempts',
  difficulty: 'admin.items.sort.difficulty',
  discrimination: 'admin.items.sort.discrimination',
  time: 'admin.items.sort.time',
  dropOff: 'admin.items.sort.dropOff',
} as const satisfies Record<ItemSortKey, MessageKey>;

/**
 * Admin page: how each question of one exam performed among the released results (FR-37), with the difficulty and discrimination indices, the
 * average time, and where candidates stopped. A question's figures are not shown until enough candidates had it; the page says so rather than
 * showing a figure drawn from a few answers.
 */
@Component({
  selector: 'app-item-analysis',
  imports: [RouterLink, RefreshButton, TranslatePipe],
  templateUrl: './item-analysis.html',
  styleUrl: './item-analysis.css',
})
export class ItemAnalysis {
  private readonly api = inject(AnalyticsApiService);
  private readonly i18n = inject(I18nService);
  private readonly document = inject(DOCUMENT);
  protected readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('id');

  /** The format whose download is in flight, or null. While one runs, every download button is disabled, so no second export can start. */
  protected readonly exporting = signal<ReportFormat | null>(null);
  protected readonly exportError = signal<string | null>(null);

  protected readonly sortKeys = ITEM_SORT_KEYS;
  protected readonly view = signal<ItemAnalysisView>('loading');
  /** Whether the analysis is being read, for the refresh icon to turn. */
  protected readonly refreshing = signal(false);
  protected readonly analysis = signal<ExamItemAnalysisDto | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly sortKey = signal<ItemSortKey>('position');
  protected readonly direction = signal<SortDirection>('asc');

  protected readonly rows = computed<ItemRowDto[]>(() =>
    sortItemRows(this.analysis()?.questions ?? [], this.sortKey(), this.direction()),
  );

  constructor() {
    if (this.examId === null) {
      this.errorMessage.set(this.i18n.t('admin.items.noExam'));
      this.view.set('error');
      return;
    }
    this.load();
  }

  /** Reads the analysis again, e.g. after a failed read. */
  protected retry(): void {
    this.load();
  }

  /**
   * Reads the analysis again, so figures from results released since show without reloading the tab. The figures stay, with the sort chosen,
   * until the answer replaces them; a failed read leaves them and says why. When there is nothing on screen to keep, this is a retry.
   */
  protected onRefresh(): void {
    if (this.refreshing()) {
      return;
    }
    this.load(this.view() !== 'error' && this.view() !== 'loading');
  }

  /**
   * Downloads the analysis as a CSV or a PDF file. The server records the export before it sends the file, so a failed export leaves no file
   * behind, and the reader is told why: the server's own message when it gave one. A press while a download runs is ignored.
   */
  protected download(format: ReportFormat): void {
    const examId = this.examId;
    if (examId === null || this.exporting() !== null) {
      return;
    }

    this.exporting.set(format);
    this.exportError.set(null);
    const request = format === 'csv' ? this.api.exportItemAnalysisCsv(examId) : this.api.exportItemAnalysisPdf(examId);
    request.subscribe({
      next: (response) => {
        this.exporting.set(null);
        if (!saveDownload(response, `item-analysis.${format}`, this.document)) {
          this.exportError.set(this.i18n.t('exports.failed'));
        }
      },
      error: (error: unknown) => {
        console.error(`Exporting the item analysis as ${format} failed`, error);
        // The button stays disabled until the message is read, so a second press cannot race the message that is on its way.
        void extractBlobErrorMessage(error, this.i18n.t('exports.failed')).then((message) => {
          this.exporting.set(null);
          this.exportError.set(message);
        });
      },
    });
  }

  /** Sorts by a column; choosing the column already sorted reverses its direction. */
  protected sortBy(key: ItemSortKey): void {
    if (this.sortKey() === key) {
      this.direction.update((current) => (current === 'asc' ? 'desc' : 'asc'));
      return;
    }
    this.sortKey.set(key);
    this.direction.set('asc');
  }

  /** The word for a sort button's state, so a screen reader hears which column is sorted and how. */
  protected sortState(key: ItemSortKey): string {
    if (this.sortKey() !== key) {
      return '';
    }
    return this.direction() === 'asc' ? this.i18n.t('admin.items.sort.ascending') : this.i18n.t('admin.items.sort.descending');
  }

  protected sortLabel(key: ItemSortKey): string {
    return this.i18n.t(SORT_LABEL_KEYS[key]);
  }

  /** The difficulty as a share of the candidates who had the question, or the words for a figure that is not shown. */
  protected difficultyText(row: ItemRowDto): string {
    return row.difficulty === null ? this.i18n.t('admin.items.notShown') : `${Math.round(row.difficulty * 100)}%`;
  }

  /** The discrimination with its sign, so a negative question reads as one. Withheld with a reason where a figure is possible but cannot be made. */
  protected discriminationText(row: ItemRowDto): string {
    if (row.discrimination !== null) {
      return `${row.discrimination > 0 ? '+' : ''}${row.discrimination.toFixed(2)}`;
    }
    return row.difficulty === null ? this.i18n.t('admin.items.notShown') : this.i18n.t('admin.items.noCompare');
  }

  /** A percentage of the marks as the page shows it, or the words for a figure that is not shown. */
  protected percentText(value: number | null): string {
    return value === null ? this.i18n.t('admin.items.notShown') : `${value}%`;
  }

  /** A time in seconds as the page shows it, or the words for a figure that is not shown, so a withheld average is never read as zero. */
  protected secondsText(value: number | null): string {
    return value === null ? this.i18n.t('admin.items.notShown') : this.i18n.t('admin.items.seconds', { seconds: value });
  }

  /**
   * How many counted candidates stopped at the question, with their share of those who reached it. The count is always shown, as the attempts
   * are; only the share is withheld, and the cell says so rather than showing a blank that could be read as none.
   */
  protected droppedOffText(row: ItemRowDto): string {
    const { droppedOff, rate } = row.dropOff;
    return rate === null
      ? `${droppedOff} (${this.i18n.t('admin.items.notShown')})`
      : `${droppedOff} (${Math.round(rate * 100)}%)`;
  }

  /**
   * The label of a score band, with the open ends of the scale named in words: "Below 10%" at the bottom and "90% and above" at the top.
   */
  protected bandLabel(band: ScoreBandDto): string {
    if (band.fromPercent === null && band.toPercent !== null) {
      return this.i18n.t('admin.items.scores.bandBelow', { to: band.toPercent });
    }
    if (band.toPercent === null && band.fromPercent !== null) {
      return this.i18n.t('admin.items.scores.bandAbove', { from: band.fromPercent });
    }
    if (band.fromPercent !== null && band.toPercent !== null) {
      return this.i18n.t('admin.items.scores.band', { from: band.fromPercent, to: band.toPercent });
    }
    return this.i18n.t('admin.items.notShown');
  }

  /**
   * The width of a band's bar, as a share of the largest band, so the tallest band fills the track and the shape of the distribution reads at a
   * glance. The counts beside the bars carry the exact figures.
   */
  protected barWidth(band: ScoreBandDto, distribution: ScoreBandDto[]): number {
    const largest = Math.max(0, ...distribution.map((other) => other.count));
    return largest === 0 ? 0 : (band.count / largest) * 100;
  }

  /** The count line above the table: how many candidates the figures come from, and the size of each group. */
  protected summary(analysis: ExamItemAnalysisDto): string {
    return this.i18n.t('admin.items.summary', { count: analysis.candidateCount, group: analysis.groupSize });
  }

  /**
   * Reads the analysis. A quiet read, for the refresh icon, keeps what is on screen while the answer is on its way, and if it fails keeps it
   * still and says why above it; a read that is not quiet shows "loading" in its place.
   */
  private load(quiet = false): void {
    const examId = this.examId;
    if (examId === null) {
      return;
    }

    if (!quiet) {
      this.view.set('loading');
    }
    this.refreshing.set(true);
    this.errorMessage.set(null);
    this.api.getItemAnalysis(examId).subscribe({
      next: (analysis) => {
        this.analysis.set(analysis);
        this.refreshing.set(false);
        if (!analysis.resultsReleased) {
          this.view.set('held');
        } else {
          this.view.set(analysis.questions.length === 0 ? 'empty' : 'ready');
        }
      },
      error: (error: unknown) => {
        this.refreshing.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('admin.items.loadError')));
        if (!quiet) {
          this.view.set('error');
        }
      },
    });
  }
}
