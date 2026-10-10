/** The event a notice is about. Mirrors the API's notification kinds (FR-39). */
export type NotificationKind =
  | 'InviteReceived'
  | 'AttemptRequestReceived'
  | 'AttemptRequestApproved'
  | 'AttemptRequestDeclined'
  | 'DisputeRejected'
  | 'ExamReminder24Hours'
  | 'ExamReminderOneHour'
  | 'ResultReleased'
  | 'ScoreRevised';

/** One notice in the signed-in account's feed, as the API returns it. */
export interface NotificationDto {
  id: string;
  kind: NotificationKind;
  /** The invite, attempt, request, dispute, exam or revision the notice is about; a link target for some kinds. */
  subjectId: string;
  examName: string | null;
  createdAtUtc: string;
  readAtUtc: string | null;
  isRead: boolean;
}

/** One page of the feed, with the totals it is shown beside. */
export interface NotificationPage {
  items: NotificationDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  unreadCount: number;
}

/** How many notices one page of the feed asks for. */
export const NOTIFICATION_PAGE_SIZE = 20;
