import { Injectable, inject, signal } from '@angular/core';
import { NotificationsApiService } from './notifications-api.service';

/**
 * The unread count the header shows beside "Notifications" (FR-39). One shared value, so the header and the feed page agree: a page that
 * marks notices read updates it at once, without waiting for the next navigation.
 */
@Injectable({ providedIn: 'root' })
export class NotificationsStateService {
  private readonly api = inject(NotificationsApiService);

  /** How many notices are unread; zero when nobody is signed in. */
  readonly unreadCount = signal(0);

  /** Reads the count again. A failed read keeps the last known count: the badge is a convenience, and must not break the page. */
  refresh(): void {
    this.api.unreadCount().subscribe({
      next: (count) => this.unreadCount.set(count),
      error: (error: unknown) => {
        console.warn('The unread notification count could not be read; the last known count is shown.', error);
      },
    });
  }

  /** Forgets the count, for when nobody is signed in. */
  clear(): void {
    this.unreadCount.set(0);
  }
}
