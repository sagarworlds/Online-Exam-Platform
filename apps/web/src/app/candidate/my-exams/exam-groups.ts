import { MyExamDto } from '../candidate.models';

/** The exams of the My exams page, divided by what the candidate can do with them now. */
export interface ExamGroups {
  /**
   * Exams the candidate can act on now: carry on with one in progress, start one, or ask for another attempt. An exam in progress comes
   * first, then the one that closes soonest, so what is most urgent is at the top.
   */
  available: MyExamDto[];
  /** Exams that have not opened yet and have no attempt, the one that opens first at the top. */
  upcoming: MyExamDto[];
  /** Everything else: exams already attempted, and exams that closed without an attempt, the most recent first. Nothing to do with them now. */
  attempted: MyExamDto[];
}

/** Whether an attempt at the exam is open: the candidate can carry on with it. */
function inProgress(exam: MyExamDto): boolean {
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

/** The most recent time the candidate did something at the exam; when they never did, the end of its window. */
function lastActivity(exam: MyExamDto): number {
  const times = exam.attempts.map((attempt) => Date.parse(attempt.submittedAtUtc ?? attempt.startedAtUtc));
  return times.length > 0 ? Math.max(...times) : Date.parse(exam.endUtc);
}

/**
 * Divides the candidate's exams into what they can act on now, what is coming, and what is done. New exams are therefore always above the
 * ones already attempted, whatever order the API lists them in. The input is not changed.
 *
 * @param exams The candidate's exams, in any order.
 * @returns The exams of each group, each group in the order described on {@link ExamGroups}.
 */
export function groupExams(exams: readonly MyExamDto[]): ExamGroups {
  const available: MyExamDto[] = [];
  const upcoming: MyExamDto[] = [];
  const attempted: MyExamDto[] = [];

  for (const exam of exams) {
    if (needsAction(exam)) {
      available.push(exam);
    } else if (exam.state === 'NotOpen' && exam.attempts.length === 0) {
      upcoming.push(exam);
    } else {
      attempted.push(exam);
    }
  }

  available.sort((a, b) => Number(inProgress(b)) - Number(inProgress(a)) || Date.parse(a.endUtc) - Date.parse(b.endUtc));
  upcoming.sort((a, b) => Date.parse(a.startUtc) - Date.parse(b.startUtc));
  attempted.sort((a, b) => lastActivity(b) - lastActivity(a));

  return { available, upcoming, attempted };
}
