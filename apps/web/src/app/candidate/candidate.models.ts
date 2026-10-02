/** Whether an attempt is still open or already submitted and scored. */
export type AttemptStatus = 'InProgress' | 'Submitted';

/** Whether a candidate can start an exam right now: not yet, yes, or the window (or its late-entry cutoff) has passed. */
export type MyExamState = 'NotOpen' | 'Open' | 'Closed';

/** An exam the candidate is enrolled in, as listed on their exams page. All instants are UTC. */
export interface MyExamDto {
  examId: string;
  name: string;
  description: string | null;
  startUtc: string;
  endUtc: string;
  lateEntryDeadlineUtc: string | null;
  durationSeconds: number | null;
  questionCount: number;
  state: MyExamState;
  /** The candidate's attempt at this exam, or null if they have not started it. */
  attemptId: string | null;
  attemptStatus: AttemptStatus | null;
  /** The marks scored and available, once the attempt is submitted. */
  score: number | null;
  maxScore: number | null;
}

/** An answer option as the candidate sees it. The API never says which one is correct. */
export interface AttemptOptionDto {
  id: string;
  text: string;
}

/** A question as the candidate sees it, with the option they have saved so far. */
export interface AttemptQuestionDto {
  id: string;
  text: string;
  options: AttemptOptionDto[];
  selectedOptionId: string | null;
}

/** A section of the exam. */
export interface AttemptSectionDto {
  id: string;
  name: string;
  questions: AttemptQuestionDto[];
}

/** An attempt: the questions while it is open, the result once it is submitted. All instants are UTC. */
export interface AttemptDto {
  id: string;
  examId: string;
  examName: string;
  status: AttemptStatus;
  startedAtUtc: string;
  /** When the server will close the attempt, whatever the candidate's own clock says. */
  deadlineUtc: string;
  submittedAtUtc: string | null;
  /** Whether the attempt ended because time ran out rather than because the candidate submitted it. */
  autoSubmitted: boolean;
  score: number | null;
  maxScore: number | null;
  /** The server's clock when this was produced; lets the countdown ignore the candidate's clock error. */
  serverTimeUtc: string;
  /** Empty once the attempt is submitted. */
  sections: AttemptSectionDto[];
}
