import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { AnalyticsApiService } from '../analytics-api.service';
import { CandidateAnalyticsDto, ScorePointDto } from '../analytics.models';
import { plotTrend, TREND_MARGINS, TREND_SIZE, TrendPlot, TrendPoint } from '../trend-plot';

/** What the page shows at any moment: the figures are being read, they are here, no result is released yet, or they could not be read. */
export type AnalyticsView = 'loading' | 'ready' | 'empty' | 'error';

/** Where a drawn point of the trend sits, and the released result it stands for. */
export interface DrawnResult {
  point: TrendPoint;
  result: ScorePointDto;
}

/**
 * Where a candidate reads their own performance across the exams they sat (FR-36): the score trend, the results in order (which is also the
 * table behind the chart), and the answers by section, weakest first. Only released results are in it, because the API counts nothing else.
 */
@Component({
  selector: 'app-candidate-analytics',
  imports: [DatePipe, RouterLink, TranslatePipe],
  templateUrl: './candidate-analytics.html',
  styleUrl: './candidate-analytics.css',
})
export class CandidateAnalytics {
  private readonly api = inject(AnalyticsApiService);
  private readonly i18n = inject(I18nService);

  protected readonly size = TREND_SIZE;
  protected readonly margins = TREND_MARGINS;

  protected readonly view = signal<AnalyticsView>('loading');
  protected readonly analytics = signal<CandidateAnalyticsDto | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  /** The index into `drawn()` of the point under the pointer, or null when none is. */
  protected readonly hovered = signal<number | null>(null);

  protected readonly trend = computed(() => this.analytics()?.trend ?? []);
  protected readonly plot = computed<TrendPlot>(() => plotTrend(this.trend().map((result) => result.percentOfMarks)));

  /** Each drawn point paired with its result, so the tooltip and the end label can name the exam. */
  protected readonly drawn = computed<DrawnResult[]>(() =>
    this.plot().points.map((point) => ({ point, result: this.trend()[point.index] })),
  );

  /** The most recent result that has a share of the marks, which is the one the headline and the end label show. */
  protected readonly latest = computed<DrawnResult | null>(() => this.drawn().at(-1) ?? null);
  protected readonly hoveredResult = computed<DrawnResult | null>(() => {
    const index = this.hovered();
    return index === null ? null : (this.drawn()[index] ?? null);
  });

  constructor() {
    this.load();
  }

  /** Reads the figures again, e.g. after a failed read. */
  protected retry(): void {
    this.load();
  }

  /** The share of the marks as shown, or the words for a result whose exam had no marks to earn. */
  protected percent(value: number | null): string {
    return value === null ? this.i18n.t('analytics.noMarks') : `${value}%`;
  }

  /** How many released results a section name was in, in words: "In 1 result" or "In 3 results". */
  protected resultCountText(count: number): string {
    return this.i18n.plural('analytics.sections.resultCount', count);
  }

  /**
   * Where the tooltip's left edge goes: centred on its point, but clamped inside the plot, so a point at either end of the chart never pushes
   * the tooltip past the edge of a phone and into a sideways scroll. The widths match the CSS.
   */
  protected tipLeft(point: TrendPoint): string {
    return `clamp(0px, calc(${this.percentX(point)}% - 6rem), calc(100% - 12rem))`;
  }

  /** Where the end label's left edge goes, clamped the same way for a label about five rem wide. */
  protected endLeft(point: TrendPoint): string {
    return `clamp(0px, calc(${this.percentX(point)}% - 2.5rem), calc(100% - 5rem))`;
  }

  private percentX(point: TrendPoint): number {
    return (point.x / this.size.width) * 100;
  }

  /** Opens the tooltip of the point at this index while the pointer is over it. Keyboard readers have the results list instead. */
  protected show(index: number): void {
    this.hovered.set(index);
  }

  private load(): void {
    this.view.set('loading');
    this.errorMessage.set(null);
    this.api.getMyAnalytics().subscribe({
      next: (analytics) => {
        this.analytics.set(analytics);
        this.hovered.set(null);
        this.view.set(analytics.resultCount === 0 ? 'empty' : 'ready');
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('analytics.loadError')));
        this.view.set('error');
      },
    });
  }
}
