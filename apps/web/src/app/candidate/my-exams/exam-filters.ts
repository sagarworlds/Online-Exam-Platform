import { MyExamDto } from '../candidate.models';
import { groupOf, inProgress } from './exam-groups';

/**
 * What the candidate can narrow their list to besides typing: every exam, the one they have started, the ones closing within a day, the ones
 * still to open, or the ones that are done.
 */
export type ExamFilter = 'all' | 'inProgress' | 'closingSoon' | 'notOpen' | 'done';

/** The filters in the order they are offered. */
export const EXAM_FILTERS: readonly ExamFilter[] = ['all', 'inProgress', 'closingSoon', 'notOpen', 'done'];

/** How close to its end an exam is "closing soon": a day, the point at which a candidate still has time but should not leave it to tomorrow. */
export const CLOSING_SOON_MS = 24 * 60 * 60 * 1000;

/**
 * Whether an exam fits what the candidate typed. Every word they typed must appear in the exam's name or description, in any order and in
 * any case, so "maths final" finds "Final exam: Maths". Nothing typed fits every exam.
 *
 * @param exam The exam.
 * @param query What the candidate typed.
 * @returns Whether the exam is one they could mean.
 */
export function matchesQuery(exam: MyExamDto, query: string): boolean {
  const words = query.toLowerCase().split(/\s+/).filter((word) => word.length > 0);
  if (words.length === 0) {
    return true;
  }
  const text = `${exam.name} ${exam.description ?? ''}`.toLowerCase();
  return words.every((word) => text.includes(word));
}

/**
 * Whether an exam fits one of the filters. "Closing soon" is only an exam the candidate can still do something about, so a closed exam, or
 * one already attempted with nothing left to do, is not nagging them from it.
 *
 * @param exam The exam.
 * @param filter The filter.
 * @param nowMs The moment to judge "closing soon" from, in milliseconds.
 * @returns Whether the filter lets the exam through.
 */
export function matchesFilter(exam: MyExamDto, filter: ExamFilter, nowMs: number): boolean {
  switch (filter) {
    case 'all':
      return true;
    case 'inProgress':
      return inProgress(exam);
    case 'closingSoon': {
      const untilEnd = Date.parse(exam.endUtc) - nowMs;
      return exam.state === 'Open' && groupOf(exam) === 'available' && untilEnd > 0 && untilEnd <= CLOSING_SOON_MS;
    }
    case 'notOpen':
      return exam.state === 'NotOpen';
    case 'done':
      return groupOf(exam) === 'attempted';
  }
}

/**
 * How many exams each filter would show, so a candidate sees what a chip holds before pressing it.
 *
 * @param exams The exams to count, already narrowed by whatever the candidate typed.
 * @param nowMs The moment to judge "closing soon" from, in milliseconds.
 * @returns A count for every filter.
 */
export function countByFilter(exams: readonly MyExamDto[], nowMs: number): Record<ExamFilter, number> {
  const counts = { all: 0, inProgress: 0, closingSoon: 0, notOpen: 0, done: 0 };
  for (const filter of EXAM_FILTERS) {
    counts[filter] = exams.filter((exam) => matchesFilter(exam, filter, nowMs)).length;
  }
  return counts;
}
