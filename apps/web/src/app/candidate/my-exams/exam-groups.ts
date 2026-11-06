import { MyExamDto } from '../candidate.models';

/** The exams of the My exams page, divided by what the candidate can do with them now. */
export interface ExamGroups {
  /**
   * Exams the candidate can act on now: carry on with one in progress, start one, or ask for another attempt. An exam in progress is always
   * first, so carrying on with it is never a search; the rest follow the chosen {@link ExamSort}.
   */
  available: MyExamDto[];
  /** Exams that have not opened yet and have no attempt. */
  upcoming: MyExamDto[];
  /** Everything else: exams already attempted, and exams that closed without an attempt. Nothing to do with them now. */
  attempted: MyExamDto[];
}

/** One of the groups of {@link ExamGroups}. */
export type ExamGroupId = keyof ExamGroups;

/**
 * How the exams are put in order inside each group.
 *
 * - `newest`: the exam made most recently first. What the page opens with, because a new exam is the one a candidate has come to find.
 * - `soonest`: what needs attention first. The exam that closes soonest among those to take, the one that opens first among those coming,
 *   and the exam last worked on first among those that are done.
 * - `name`: alphabetical, numbers by their value ("Test 2" before "Test 10").
 */
export type ExamSort = 'newest' | 'soonest' | 'name';

/** The order the page opens in. */
export const DEFAULT_EXAM_SORT: ExamSort = 'newest';

/** Whether an attempt at the exam is open: the candidate can carry on with it. */
export function inProgress(exam: MyExamDto): boolean {
  return exam.attempts.some((attempt) => attempt.status === 'InProgress');
}

/**
 * Whether the candidate can do something about the exam now. An open exam that was never attempted counts even when the API will not let
 * them start it, because its card says why (the question paper is locked, say), and the candidate looks for it among the new exams.
 */
function needsAction(exam: MyExamDto): boolean {
  return (
    inProgress(exam) ||
    exam.canStartAttempt ||
    exam.canRequestAttempt ||
    exam.attemptRequest?.status === 'Pending' ||
    (exam.state === 'Open' && exam.attempts.length === 0)
  );
}

/**
 * The group an exam belongs to: what the candidate can act on now, what is still to open, or what is done.
 *
 * @param exam The exam.
 * @returns Its group; the same answer for the same exam wherever it is asked, so a filter and the page's sections can never disagree.
 */
export function groupOf(exam: MyExamDto): ExamGroupId {
  if (needsAction(exam)) {
    return 'available';
  }
  return exam.state === 'NotOpen' && exam.attempts.length === 0 ? 'upcoming' : 'attempted';
}

/** The most recent time the candidate did something at the exam; when they never did, the end of its window. */
function lastActivity(exam: MyExamDto): number {
  const times = exam.attempts.map((attempt) => Date.parse(attempt.submittedAtUtc ?? attempt.startedAtUtc));
  return times.length > 0 ? Math.max(...times) : Date.parse(exam.endUtc);
}

/** When the exam was made, in milliseconds; 0 from an API that does not say yet, so those exams fall back on their window. */
function createdAt(exam: MyExamDto): number {
  const time = Date.parse(exam.createdAtUtc ?? '');
  return Number.isNaN(time) ? 0 : time;
}

/** Alphabetical by name, ignoring case and accents, with numbers read as numbers. */
function byName(a: MyExamDto, b: MyExamDto): number {
  return a.name.localeCompare(b.name, undefined, { sensitivity: 'base', numeric: true });
}

/** The exam made most recently first; exams made at the same moment (or with no date) by the later window, then by name. */
function byNewest(a: MyExamDto, b: MyExamDto): number {
  return createdAt(b) - createdAt(a) || Date.parse(b.startUtc) - Date.parse(a.startUtc) || byName(a, b);
}

/** The comparison that puts a group's exams in the order of `sort`. */
function comparer(group: ExamGroupId, sort: ExamSort): (a: MyExamDto, b: MyExamDto) => number {
  if (sort === 'name') {
    return byName;
  }
  if (sort === 'newest') {
    return byNewest;
  }
  switch (group) {
    case 'available':
      return (a, b) => Date.parse(a.endUtc) - Date.parse(b.endUtc);
    case 'upcoming':
      return (a, b) => Date.parse(a.startUtc) - Date.parse(b.startUtc);
    default:
      return (a, b) => lastActivity(b) - lastActivity(a);
  }
}

/**
 * Divides the candidate's exams into what they can act on now, what is coming, and what is done, and puts each group in order. New exams
 * are therefore always above the ones already attempted, whatever order the API lists them in, and an exam in progress is always at the
 * top of what can be done. The input is not changed.
 *
 * @param exams The candidate's exams, in any order.
 * @param sort How to order the exams inside each group; the newest first when not given.
 * @returns The exams of each group, each group in the order described on {@link ExamGroups} and {@link ExamSort}.
 */
export function groupExams(exams: readonly MyExamDto[], sort: ExamSort = DEFAULT_EXAM_SORT): ExamGroups {
  const groups: ExamGroups = { available: [], upcoming: [], attempted: [] };

  for (const exam of exams) {
    groups[groupOf(exam)].push(exam);
  }

  const available = comparer('available', sort);
  groups.available.sort((a, b) => Number(inProgress(b)) - Number(inProgress(a)) || available(a, b));
  groups.upcoming.sort(comparer('upcoming', sort));
  groups.attempted.sort(comparer('attempted', sort));

  return groups;
}
