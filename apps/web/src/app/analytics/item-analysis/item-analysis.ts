import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type { MessageKey } from '../../i18n/messages.en';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { AnalyticsApiService } from '../analytics-api.service';
import { ExamItemAnalysisDto, ItemRowDto } from '../analytics.models';
import { ItemSortKey, SortDirection, sortItemRows } from './item-analysis-sort';

/** What the page shows at any moment: being read, the questions, the results held, nothing to show, or a failed read. */
export type ItemAnalysisView = 'loading' | 'ready' | 'held' | 'empty' | 'error';

/** The columns the reader can sort by, in the order the sort buttons show them. */
export const ITEM_SORT_KEYS: readonly ItemSortKey[] = ['position', 'attempts', 'difficulty', 'discrimination'];

/** The message that names each sort column; typed so a key missing from the messages does not compile. */
const SORT_LABEL_KEYS = {
  position: 'admin.items.sort.position',
  attempts: 'admin.items.sort.attempts',
  difficulty: 'admin.items.sort.difficulty',
  discrimination: 'admin.items.sort.discrimination',
} as const satisfies Record<ItemSortKey, MessageKey>;

/**
 * Admin page: how each question of one exam performed among the released results (FR-37), with the difficulty and discrimination indices. A
 * question's indices are not shown until enough candidates had it; the page says so rather than showing a figure drawn from a few answers.
 */
@Component({
  selector: 'app-item-analysis',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './item-analysis.html',
  styleUrl: './item-analysis.css',
})
export class ItemAnalysis {
  private readonly api = inject(AnalyticsApiService);
  private readonly i18n = inject(I18nService);
  protected readonly examId = inject(ActivatedRoute).snapshot.paramMap.get('id');

  protected readonly sortKeys = ITEM_SORT_KEYS;
  protected readonly view = signal<ItemAnalysisView>('loading');
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

  /** The count line above the table: how many candidates the figures come from, and the size of each group. */
  protected summary(analysis: ExamItemAnalysisDto): string {
    return this.i18n.t('admin.items.summary', { count: analysis.candidateCount, group: analysis.groupSize });
  }

  private load(): void {
    const examId = this.examId;
    if (examId === null) {
      return;
    }

    this.view.set('loading');
    this.errorMessage.set(null);
    this.api.getItemAnalysis(examId).subscribe({
      next: (analysis) => {
        this.analysis.set(analysis);
        if (!analysis.resultsReleased) {
          this.view.set('held');
        } else {
          this.view.set(analysis.questions.length === 0 ? 'empty' : 'ready');
        }
      },
      error: (error: unknown) => {
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('admin.items.loadError')));
        this.view.set('error');
      },
    });
  }
}
