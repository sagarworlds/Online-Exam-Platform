import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { MessageKey } from '../../i18n/messages.en';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { NotificationsApiService } from '../notifications-api.service';
import { NotificationsStateService } from '../notifications-state.service';
import { NOTIFICATION_PAGE_SIZE, NotificationDto, NotificationKind } from '../notifications.models';

/** The sentence that says what each kind of notice is, with the exam's name in it. */
const KIND_MESSAGES: Record<NotificationKind, MessageKey> = {
  InviteReceived: 'notifications.kind.InviteReceived',
  AttemptRequestReceived: 'notifications.kind.AttemptRequestReceived',
  AttemptRequestApproved: 'notifications.kind.AttemptRequestApproved',
  AttemptRequestDeclined: 'notifications.kind.AttemptRequestDeclined',
  DisputeRejected: 'notifications.kind.DisputeRejected',
  DisputeAccepted: 'notifications.kind.DisputeAccepted',
  ExamReminder24Hours: 'notifications.kind.ExamReminder24Hours',
  ExamReminderOneHour: 'notifications.kind.ExamReminderOneHour',
  ResultReleased: 'notifications.kind.ResultReleased',
  ScoreRevised: 'notifications.kind.ScoreRevised',
};

/**
 * The signed-in account's own feed of notices (FR-39): unread first, newest first within each group, a page at a time. Marking a notice
 * read keeps it in place until the feed is read again, so the list does not jump under the reader's pointer.
 */
@Component({
  selector: 'app-notification-feed',
  imports: [DatePipe, RouterLink, TranslatePipe],
  templateUrl: './notification-feed.html',
  styleUrl: './notification-feed.css',
})
export class NotificationFeed {
  private readonly api = inject(NotificationsApiService);
  private readonly i18n = inject(I18nService);
  protected readonly state = inject(NotificationsStateService);

  protected readonly items = signal<NotificationDto[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(0);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly announcement = signal('');
  protected readonly markingId = signal<string | null>(null);
  protected readonly markingAll = signal(false);

  /** Whether the server holds notices the list has not shown yet. */
  protected readonly hasMore = computed(() => this.items().length < this.totalCount());

  /** Whether any action is in flight, so the controls that would race it are held. */
  protected readonly busy = computed(() => this.markingAll() || this.markingId() !== null);

  constructor() {
    this.load(1);
  }

  /** The sentence for one notice, with its exam's name; a notice whose exam can no longer be read says "this exam". */
  protected text(notice: NotificationDto): string {
    return this.i18n.t(KIND_MESSAGES[notice.kind], {
      exam: notice.examName ?? this.i18n.t('notifications.theExam'),
    });
  }

  /** Where the "Open" link goes for a notice, or null when there is nowhere useful to send the reader. */
  protected link(notice: NotificationDto): string[] | null {
    switch (notice.kind) {
      case 'InviteReceived':
        return ['/invite'];
      case 'AttemptRequestReceived':
        return ['/admin/attempt-requests'];
      case 'ResultReleased':
        return ['/attempt', notice.subjectId, 'review'];
      default:
        return ['/my-exams'];
    }
  }

  /** Reads the first page again, replacing what is shown; the retry after a failed read uses it too. */
  protected reload(): void {
    this.load(1);
  }

  protected loadMore(): void {
    if (this.loadingMore() || !this.hasMore()) {
      return;
    }

    this.load(this.page() + 1);
  }

  protected markRead(notice: NotificationDto): void {
    if (this.busy() || notice.isRead) {
      return;
    }

    this.actionError.set(null);
    this.markingId.set(notice.id);
    this.api.markRead(notice.id).subscribe({
      next: (updated) => {
        this.items.update((list) => list.map((item) => (item.id === updated.id ? updated : item)));
        this.markingId.set(null);
        this.state.refresh();
        this.announcement.set(this.i18n.t('notifications.markedRead'));
      },
      error: (error: unknown) => {
        this.markingId.set(null);
        this.actionError.set(extractErrorMessage(error));
      },
    });
  }

  protected markAll(): void {
    if (this.busy()) {
      return;
    }

    this.actionError.set(null);
    this.markingAll.set(true);
    this.api.markAllRead().subscribe({
      next: (marked) => {
        const readAt = new Date().toISOString();
        this.items.update((list) => list.map((item) => ({ ...item, isRead: true, readAtUtc: item.readAtUtc ?? readAt })));
        this.markingAll.set(false);
        this.state.unreadCount.set(0);
        this.announcement.set(this.i18n.t('notifications.allMarkedRead', { count: marked }));
      },
      error: (error: unknown) => {
        this.markingAll.set(false);
        this.actionError.set(extractErrorMessage(error));
      },
    });
  }

  private load(page: number): void {
    const firstPage = page === 1;
    if (firstPage) {
      this.loading.set(true);
      this.errorMessage.set(null);
    } else {
      this.loadingMore.set(true);
    }

    this.api.list(page, NOTIFICATION_PAGE_SIZE).subscribe({
      next: (result) => {
        // A later page is added to the end; a notice already shown (a new one can push a row down) is not shown twice.
        const shown = firstPage ? [] : this.items();
        const seen = new Set(shown.map((item) => item.id));
        this.items.set([...shown, ...result.items.filter((item) => !seen.has(item.id))]);
        this.totalCount.set(result.totalCount);
        this.page.set(result.page);
        this.state.unreadCount.set(result.unreadCount);
        this.loading.set(false);
        this.loadingMore.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadingMore.set(false);
        if (firstPage) {
          this.errorMessage.set(extractErrorMessage(error));
        } else {
          this.actionError.set(extractErrorMessage(error));
        }
      },
    });
  }
}
