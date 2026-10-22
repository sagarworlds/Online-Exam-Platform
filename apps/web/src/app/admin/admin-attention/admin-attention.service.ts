import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';
import { AttemptAdminApiService } from '../../attempt-management/attempt-admin-api.service';
import { AttentionKey, Permission } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { ExamApiService } from '../../exam-authoring/exam-api.service';
import { ProctoringApiService } from '../../proctoring/proctoring-api.service';

/** How many rows a queue's list holds at most (the API stops there), so a count that reaches it is "at least". */
export const QUEUE_LIMIT = 200;

/** How many of a queue's latest items the admin home lists under its tile. */
export const LATEST_ITEMS = 3;

/** One thing that waits, as a line under its tile: who or what, and the page that deals with it. */
export interface AttentionItem {
  id: string;
  title: string;
  /** A second line: the exam, or the score. Null when there is nothing to add. */
  subtitle: string | null;
  /** The page that deals with it: its queue, or its exam's own page. */
  path: string;
}

/** How one count stands: not yet read, read, or not readable. */
export type AttentionState = { status: 'loading' } | { status: 'ready'; count: number; atLimit: boolean } | { status: 'failed' };

/** How old a count may be before moving to another page reads it again. */
const STALE_AFTER_MS = 60_000;

interface Source {
  /** The permission that lets the user read the list, so a count is asked for only by someone the API would answer. */
  permission: string;
  /** The rows that need attention, counted. */
  read: () => Observable<{ count: number; atLimit: boolean; items: AttentionItem[] }>;
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
  private readonly proctoring = inject(ProctoringApiService);

  /** Each count that has been asked for. A key is absent when the user may not read it. */
  readonly counts = signal<Readonly<Partial<Record<AttentionKey, AttentionState>>>>({});

  /** The first few things waiting in each queue, for the lines under a tile. A key is absent when its count is. */
  readonly latest = signal<Readonly<Partial<Record<AttentionKey, readonly AttentionItem[]>>>>({});

  /** Whether the counts are being read again. */
  readonly refreshing = signal(false);

  private loadedAt = 0;

  /** Which sign-in the counts belong to: a read that was on its way when someone signed out must not write into the next person's. */
  private generation = 0;

  private readonly sources: Record<AttentionKey, Source> = {
    attemptRequests: {
      permission: Permission.ExamManage,
      read: () => this.queue(this.attempts.listAttemptRequests('pending'), (row) => this.line(row, '/admin/attempt-requests')),
    },
    disputes: { permission: Permission.ExamManage, read: () => this.queue(this.attempts.listDisputes('open'), (row) => this.line(row, '/admin/disputes')) },
    issues: { permission: Permission.ExamManage, read: () => this.queue(this.attempts.listIssueReports('open'), (row) => this.line(row, '/admin/issue-reports')) },
    riskFlags: {
      permission: Permission.ProctoringReview,
      // The API counts across every exam, so this is exact: no list is cut at the queue limit.
      read: () =>
        this.proctoring.listOpenRiskFlags(LATEST_ITEMS).pipe(
          map((open) => ({
            count: open.total,
            atLimit: false,
            items: open.items.map((flag) => ({
              id: flag.id,
              title: flag.examName ?? '–',
              subtitle: `${flag.score} / ${flag.maxScore}`,
              path: `/exams/${flag.examId}/risk-flags`,
            })),
          })),
        ),
    },
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
            const items = running.slice(0, LATEST_ITEMS).map((exam) => ({ id: exam.id, title: exam.name, subtitle: null, path: `/exams/${exam.id}` }));
            return { count: running.length, atLimit: exams.length >= QUEUE_LIMIT, items };
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
      this.latest.set({});
      return;
    }

    this.refreshing.set(true);
    const generation = this.generation;
    // A count never read shows as loading; one that was read keeps its number until the new one arrives.
    this.counts.update((known) => Object.fromEntries(keys.map((key) => [key, known[key] ?? { status: 'loading' }])));

    forkJoin(
      keys.map((key) =>
        this.sources[key].read().pipe(
          map((result): [AttentionKey, AttentionState, readonly AttentionItem[]] => [key, { status: 'ready', count: result.count, atLimit: result.atLimit }, result.items]),
          // One queue that cannot be read must not hide the others; it says so, and the old number, if any, is not kept as if it were current.
          catchError(() => of<[AttentionKey, AttentionState, readonly AttentionItem[]]>([key, { status: 'failed' }, []])),
        ),
      ),
    ).subscribe((results) => {
      if (generation !== this.generation) {
        return;
      }
      this.counts.set(Object.fromEntries(results.map(([key, state]) => [key, state])));
      this.latest.set(Object.fromEntries(results.filter(([, state]) => state.status === 'ready').map(([key, , items]) => [key, items])));
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
    // The lines under the tile drop the ones dealt with, which are the oldest first on a queue's list.
    this.latest.update((known) => (known[key] === undefined ? known : { ...known, [key]: known[key].slice(count) }));
    // Not read again on the next page change as if nothing had happened: whatever else changed meanwhile is worth reading too.
    this.loadedAt = 0;
  }

  /** Forgets every count, for when someone signs out: the next person must not see the last one's. */
  clear(): void {
    this.generation++;
    this.counts.set({});
    this.latest.set({});
    this.loadedAt = 0;
    this.refreshing.set(false);
  }

  private queue<T>(rows: Observable<readonly T[]>, toItem: (row: T) => AttentionItem): Observable<{ count: number; atLimit: boolean; items: AttentionItem[] }> {
    return rows.pipe(map((list) => ({ count: list.length, atLimit: list.length >= QUEUE_LIMIT, items: list.slice(0, LATEST_ITEMS).map(toItem) })));
  }

  /** A line for a row of a queue: the candidate when there is one, else the exam, with the exam as the second line. */
  private line(row: { id: string; candidateEmail: string | null; examName: string | null }, path: string): AttentionItem {
    return { id: row.id, title: row.candidateEmail ?? row.examName ?? '–', subtitle: row.candidateEmail === null ? null : row.examName, path };
  }
}
