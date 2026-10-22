import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { ListChip, ListToolbar } from '../../shared/list-shell/list-toolbar';
import { LoadMore } from '../../shared/list-shell/load-more';
import { syncQueryParam } from '../../shared/list-shell/query-state';
import { RefreshButton } from '../../shared/refresh-button/refresh-button';
import { StatusBadge } from '../../shared/status-badge/status-badge';
import { ExamTimePipe } from '../exam-time/exam-time';
import { extractErrorMessage } from '../../shared/problem-details';
import { ExamApiService } from '../exam-api.service';
import { ExamDto, ExamStatus } from '../exam.models';
import { describeScope } from '../exam-scope-fields/exam-scope';

/** How many exams are drawn before "Load more". */
const PAGE_SIZE = 20;

/** Admin page: the exams, each linking to its editor, found by name or description and narrowed by status. */
@Component({
  selector: 'app-exam-list',
  imports: [RouterLink, ExamTimePipe, ListToolbar, LoadMore, RefreshButton, StatusBadge, TranslatePipe],
  templateUrl: './exam-list.html',
})
export class ExamList {
  private readonly examApi = inject(ExamApiService);
  protected readonly i18n = inject(I18nService);

  protected readonly exams = signal<ExamDto[]>([]);
  protected readonly loading = signal(true);
  /** Whether the list is being read again because the refresh icon was pressed; the exams stay until the answer comes. */
  protected readonly refreshing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly describeScope = describeScope;

  /** The search and the status filter; both live in the address (`?q=&status=`) so a filtered list can be shared. */
  protected readonly search = signal('');
  protected readonly status = signal<ExamStatus | ''>('');
  protected readonly statuses: readonly ExamStatus[] = ['Draft', 'Published', 'Archived'];
  private readonly pages = signal(1);

  protected readonly matching = computed(() => {
    const text = this.search().trim().toLowerCase();
    const status = this.status();
    return this.exams().filter(
      (exam) => (status === '' || exam.status === status) && (text === '' || `${exam.name} ${exam.description ?? ''}`.toLowerCase().includes(text)),
    );
  });
  protected readonly shown = computed(() => this.matching().slice(0, this.pages() * PAGE_SIZE));
  protected readonly chips = computed<ListChip[]>(() =>
    this.status() === '' ? [] : [{ key: 'status', label: this.i18n.t('exams.list.chip.status', { value: this.statusText(this.status() as ExamStatus) }) }],
  );
  /** Said only when the list is narrowed or cut short, so a short, whole list has no extra line. */
  protected readonly count = computed(() =>
    this.matching().length === this.exams().length && this.shown().length === this.matching().length
      ? ''
      : this.i18n.t('list.showing', { shown: this.shown().length, total: this.matching().length }),
  );

  protected statusLabel(exam: ExamDto): string {
    return this.statusText(exam.status);
  }

  protected statusText(status: ExamStatus): string {
    return this.i18n.t(`exams.status.${status}` as MessageKey);
  }

  constructor() {
    syncQueryParam('q', this.search);
    syncQueryParam('status', this.status, { allowed: this.statuses });
    this.read();
  }

  protected resetPage(): void {
    this.pages.set(1);
  }

  protected showMore(): void {
    this.pages.update((pages) => pages + 1);
  }

  protected clearFilter(): void {
    this.status.set('');
    this.resetPage();
  }

  /** Reads the exams again, so a change made elsewhere shows without reloading the tab. A failed read leaves the list and says why. */
  protected onRefresh(): void {
    if (this.refreshing()) {
      return;
    }

    this.errorMessage.set(null);
    this.refreshing.set(true);
    this.read();
  }

  private read(): void {
    this.examApi.getExams().subscribe({
      next: (exams) => {
        this.exams.set(exams);
        this.loading.set(false);
        this.refreshing.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.refreshing.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }
}
