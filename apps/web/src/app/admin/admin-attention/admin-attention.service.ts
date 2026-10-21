import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';
import { AttemptAdminApiService } from '../../attempt-management/attempt-admin-api.service';
import { AttentionKey, Permission } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { ExamApiService } from '../../exam-authoring/exam-api.service';

/** How many rows a queue's list holds at most (the API stops there), so a count that reaches it is "at least". */
export const QUEUE_LIMIT = 200;

/** How one count stands: not yet read, read, or not readable. */
export type AttentionState = { status: 'loading' } | { status: 'ready'; count: number; atLimit: boolean } | { status: 'failed' };

/** How old a count may be before moving to another page reads it again. */
const STALE_AFTER_MS = 60_000;

interface Source {
  /** The permission that lets the user read the list, so a count is asked for only by someone the API would answer. */
  permission: string;
  /** The rows that need attention, counted. */
  read: () => Observable<{ count: number; atLimit: boolean }>;
}

/**
 * How many things wait for staff: attempt requests to decide, disputes and reported problems to answer, and exams running now. The admin
 * home shows them as tiles and the sidebar as badges, so staff see what needs them without opening each queue.
 *
 * There is no count endpoint: a count is the length of the queue's own list, which the API gives for the waiting items only (oldest first,
 * at most {@link QUEUE_LIMIT}). A count is read for what the user's permissions open and no more, read again on the admin home and, when
 * it is a minute old, on moving to another page, and never shows something the user may not open.
 */
@Injectable({ providedIn: 'root' })
export class AdminAttentionService {
  private readonly authSession = inject(AuthSessionService);
  private readonly attempts = inject(AttemptAdminApiService);
  private readonly exams = inject(ExamApiService);

  /** Each count that has been asked for. A key is absent when the user may not read it. */
  readonly counts = signal<Readonly<Partial<Record<AttentionKey, AttentionState>>>>({});

  /** Whether the counts are being read again. */
  readonly refreshing = signal(false);

  private loadedAt = 0;

  /** Which sign-in the counts belong to: a read that was on its way when someone signed out must not write into the next person's. */
  private generation = 0;

  private readonly sources: Record<AttentionKey, Source> = {
    attemptRequests: { permission: Permission.ExamManage, read: () => this.queue(this.attempts.listAttemptRequests('pending')) },
    disputes: { permission: Permission.ExamManage, read: () => this.queue(this.attempts.listDisputes('open')) },
    issues: { permission: Permission.ExamManage, read: () => this.queue(this.attempts.listIssueReports('open')) },
    liveExams: {
      permission: Permission.ExamRead,
      read: () =>
        this.exams.getExams().pipe(
          map((exams) => {
            const now = Date.now();
            const running = exams.filter(
              (exam) => exam.status === 'Published' && exam.isScheduled && Date.parse(exam.scheduledStartTime) <= now && now < Date.parse(exam.scheduledEndTime),
            );
            // The exam list is the newest two hundred, so a full one may have running exams beyond it.
            return { count: running.length, atLimit: exams.length >= QUEUE_LIMIT };
          }),
        ),
    },
  };

  /** Reads the counts the user may see. A read already on its way is not repeated, and what was shown stays until the answer replaces it. */
  refresh(): void {
    if (this.refreshing()) {
      return;
    }

    const keys = (Object.keys(this.sources) as AttentionKey[]).filter((key) => this.authSession.hasPermission(this.sources[key].permission));
    if (keys.length === 0) {
      this.counts.set({});
      return;
    }

    this.refreshing.set(true);
    const generation = this.generation;
    // A count never read shows as loading; one that was read keeps its number until the new one arrives.
    this.counts.update((known) => Object.fromEntries(keys.map((key) => [key, known[key] ?? { status: 'loading' }])));

    forkJoin(
      keys.map((key) =>
        this.sources[key].read().pipe(
          map((result): [AttentionKey, AttentionState] => [key, { status: 'ready', ...result }]),
          // One queue that cannot be read must not hide the others; it says so, and the old number, if any, is not kept as if it were current.
          catchError(() => of<[AttentionKey, AttentionState]>([key, { status: 'failed' }])),
        ),
      ),
    ).subscribe((results) => {
      if (generation !== this.generation) {
        return;
      }
      this.counts.set(Object.fromEntries(results));
      this.loadedAt = Date.now();
      this.refreshing.set(false);
    });
  }

  /** Reads the counts again when they are a minute old or were never read, so moving about the admin area keeps the badges near current. */
  refreshIfStale(): void {
    if (Date.now() - this.loadedAt >= STALE_AFTER_MS) {
      this.refresh();
    }
  }

  /**
   * An item of a queue was dealt with on that queue's own page: its count drops at once, so the sidebar does not go on saying "1 waiting" beside
   * a queue that was just emptied, and the next read confirms it. A count that stopped at the list's limit is not changed, since it was never exact.
   */
  settled(key: AttentionKey, count = 1): void {
    this.counts.update((known) => {
      const state = known[key];
      if (state?.status !== 'ready' || state.atLimit) {
        return known;
      }
      return { ...known, [key]: { ...state, count: Math.max(0, state.count - count) } };
    });
    // Not read again on the next page change as if nothing had happened: whatever else changed meanwhile is worth reading too.
    this.loadedAt = 0;
  }

  /** Forgets every count, for when someone signs out: the next person must not see the last one's. */
  clear(): void {
    this.generation++;
    this.counts.set({});
    this.loadedAt = 0;
    this.refreshing.set(false);
  }

  private queue(rows: Observable<readonly unknown[]>): Observable<{ count: number; atLimit: boolean }> {
    return rows.pipe(map((list) => ({ count: list.length, atLimit: list.length >= QUEUE_LIMIT })));
  }
}
