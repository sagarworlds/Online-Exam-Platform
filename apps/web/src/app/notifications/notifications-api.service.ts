import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { SILENT_ACTIVITY } from '../shared/api-activity/api-activity.interceptor';
import { NotificationDto, NotificationPage } from './notifications.models';

/** Thin HTTP wrapper over the Notifications module's signed-in feed, /v1/me/notifications (FR-39). */
@Injectable({ providedIn: 'root' })
export class NotificationsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/me/notifications`;

  /** One page of the feed, unread notices first. */
  list(page: number, pageSize: number): Observable<NotificationPage> {
    return this.http.get<NotificationPage>(this.baseUrl, { params: { page, pageSize } });
  }

  /**
   * How many notices are unread. It is read in the background to keep the header count current, so it stays out of the waiting indicator
   * that would otherwise flash on every page change.
   */
  unreadCount(): Observable<number> {
    return this.http
      .get<{ unreadCount: number }>(`${this.baseUrl}/unread-count`, { context: new HttpContext().set(SILENT_ACTIVITY, true) })
      .pipe(map((body) => body.unreadCount));
  }

  /** Marks one notice read; reading one that is already read changes nothing. */
  markRead(id: string): Observable<NotificationDto> {
    return this.http.post<NotificationDto>(`${this.baseUrl}/${id}/read`, null);
  }

  /** Marks every unread notice read, and says how many there were. */
  markAllRead(): Observable<number> {
    return this.http.post<{ marked: number }>(`${this.baseUrl}/read-all`, null).pipe(map((body) => body.marked));
  }
}
