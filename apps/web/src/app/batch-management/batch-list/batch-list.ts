import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ExamApiService } from '../../exam-authoring/exam-api.service';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { ListChip, ListToolbar } from '../../shared/list-shell/list-toolbar';
import { LoadMore } from '../../shared/list-shell/load-more';
import { syncQueryParam } from '../../shared/list-shell/query-state';
import { RefreshButton } from '../../shared/refresh-button/refresh-button';
import { StatusBadge, StatusTone } from '../../shared/status-badge/status-badge';
import { BatchApiService } from '../batch-api.service';
import { BatchDto, BatchStatus } from '../batch.models';
import { LocaleDatePipe } from '../../i18n/locale-date.pipe';

/** What each state of a batch means to the person reading the list: one that is taking candidates is good, one that is over is only a fact. */
const STATUS_TONES: Record<BatchStatus, StatusTone> = { Draft: 'neutral', Active: 'success', Closed: 'info', Archived: 'neutral' };

/** How many batches are drawn before "Load more". */
const PAGE_SIZE = 20;

@Component({
  selector: 'app-batch-list',
  imports: [LocaleDatePipe, RouterLink, ListToolbar, LoadMore, RefreshButton, StatusBadge, EmptyState, TranslatePipe],
  template: `
    <div class="page page--admin">
      <div class="page-header">
        <h1>{{ 'batches.title' | t }}</h1>
        <div class="page-header__actions">
          <a routerLink="/batches/create" class="btn btn--primary">{{ 'batches.create' | t }}</a>
          <app-refresh-button [busy]="refreshing()" (refresh)="onRefresh()" />
        </div>
      </div>

      @if (loading()) {
        <p class="empty-state" role="status">{{ 'batches.loading' | t }}</p>
      }
      @if (error()) {
        <p class="error-message" role="alert">{{ error() }}</p>
      }

      @if (!loading() && batches().length === 0 && !error()) {
        <app-empty-state [title]="'batches.empty.title' | t" [message]="'batches.empty.message' | t">
          <a routerLink="/batches/create" class="btn btn--primary">{{ 'batches.empty.action' | t }}</a>
        </app-empty-state>
      }

      @if (!loading() && batches().length > 0) {
        <app-list-toolbar
          [search]="search()"
          [searchLabel]="'batches.search' | t"
          [liveSearch]="true"
          [filterable]="true"
          [chips]="chips()"
          [count]="count()"
          (searchChange)="search.set($event); resetPage()"
          (chipRemoved)="clearFilter()"
          (cleared)="clearFilter()"
        >
          <div listFilters class="field">
            <label for="filter-batch-status">{{ 'batches.col.status' | t }}</label>
            <select id="filter-batch-status" [value]="status()" (change)="status.set($any($event.target).value); resetPage()">
              <option value="">{{ 'batches.anyStatus' | t }}</option>
              @for (option of statuses; track option) {
                <option [value]="option" [selected]="option === status()">{{ statusLabel(option) }}</option>
              }
            </select>
          </div>
        </app-list-toolbar>
        @if (matching().length === 0) {
          <app-empty-state [title]="'list.noMatch' | t" />
        }
        <div class="table-wrap" role="region" tabindex="0" [attr.aria-label]="'batches.tableLabel' | t">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">{{ 'batches.col.name' | t }}</th>
                <th scope="col">{{ 'batches.col.exam' | t }}</th>
                <th scope="col">{{ 'batches.col.status' | t }}</th>
                <th scope="col">{{ 'batches.col.members' | t }}</th>
                <th scope="col">{{ 'batches.col.created' | t }}</th>
                <th scope="col">{{ 'batches.col.actions' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (batch of shown(); track batch.id) {
                <tr>
                  <th scope="row">
                    <a [routerLink]="['/batches', batch.id, 'roster']">{{ batch.name }}</a>
                  </th>
                  <td>{{ examNames().get(batch.examId) ?? '—' }}</td>
                  <td><app-status-badge [tone]="toneOf(batch.status)" [label]="statusLabel(batch.status)" /></td>
                  <td>{{ batch.activeMemberCount }} / {{ batch.maxMembers }}</td>
                  <td>{{ batch.createdAt | date: 'short' }}</td>
                  <td>
                    <a
                      [routerLink]="['/batches', batch.id, 'roster']"
                      class="btn"
                      [attr.aria-label]="'batches.manageNamed' | t: { name: batch.name }"
                      >{{ 'batches.manage' | t }}</a
                    >
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <app-load-more [hasMore]="shown().length < matching().length" (more)="showMore()" />
      }
    </div>
  `,
})
export class BatchList implements OnInit {
  private batchApi = inject(BatchApiService);
  private examApi = inject(ExamApiService);
  private i18n = inject(I18nService);

  // Signals, so that the list repaints when a read lands whatever else is going on in the page.
  protected readonly batches = signal<BatchDto[]>([]);
  /** The name of each exam a batch sits for, so the list says which exam in words. A batch whose exam is not known shows a dash. */
  protected readonly examNames = signal<ReadonlyMap<string, string>>(new Map());
  /** True for the first read only: a refresh keeps the batches on screen until the answer replaces them. */
  protected readonly loading = signal(true);
  /** Whether the batches are being read again because the refresh icon was pressed. */
  protected readonly refreshing = signal(false);
  protected readonly error = signal('');

  /** The search and the status filter, kept in the address (`?q=&status=`) so a filtered list can be shared. */
  protected readonly search = signal('');
  protected readonly status = signal<BatchStatus | ''>('');
  protected readonly statuses: readonly BatchStatus[] = ['Draft', 'Active', 'Closed', 'Archived'];
  private readonly pages = signal(1);

  protected readonly matching = computed(() => {
    const text = this.search().trim().toLowerCase();
    const status = this.status();
    return this.batches().filter(
      (batch) =>
        (status === '' || batch.status === status) &&
        (text === '' || `${batch.name} ${this.examNames().get(batch.examId) ?? ''}`.toLowerCase().includes(text)),
    );
  });
  protected readonly shown = computed(() => this.matching().slice(0, this.pages() * PAGE_SIZE));
  protected readonly chips = computed<ListChip[]>(() =>
    this.status() === '' ? [] : [{ key: 'status', label: this.i18n.t('batches.chip.status', { value: this.statusLabel(this.status() as BatchStatus) }) }],
  );
  protected readonly count = computed(() =>
    this.matching().length === this.batches().length && this.shown().length === this.matching().length
      ? ''
      : this.i18n.t('list.showing', { shown: this.shown().length, total: this.matching().length }),
  );

  constructor() {
    syncQueryParam('q', this.search);
    syncQueryParam('status', this.status, { allowed: this.statuses });
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

  ngOnInit() {
    this.loadBatches();
    // The exam names only dress the list: when they cannot be read the batches still show, with a dash for the exam.
    this.examApi.getExams().subscribe({
      next: (exams) => this.examNames.set(new Map(exams.map((exam) => [exam.id, exam.name]))),
      error: () => undefined,
    });
  }

  /** Reads the batches again, so a change made elsewhere shows without reloading the tab. A failed read leaves the list and says why. */
  protected onRefresh() {
    if (!this.refreshing()) {
      this.refreshing.set(true);
      this.loadBatches();
    }
  }

  protected toneOf(status: BatchStatus): StatusTone {
    return STATUS_TONES[status] ?? 'neutral';
  }

  protected statusLabel(status: BatchStatus): string {
    return this.i18n.t(`batches.status.${status}` as MessageKey);
  }

  loadBatches() {
    this.error.set('');
    this.batchApi.getBatches().subscribe({
      next: (data) => {
        this.batches.set(data);
        this.loading.set(false);
        this.refreshing.set(false);
      },
      error: (err) => {
        this.error.set(this.i18n.t('batches.loadFailed'));
        this.loading.set(false);
        this.refreshing.set(false);
        console.error(err);
      },
    });
  }
}
