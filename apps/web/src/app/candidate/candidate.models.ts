/** Whether an attempt is still open or already submitted and scored. */
export type AttemptStatus = 'InProgress' | 'Submitted';

/** Whether a candidate can start an exam right now: not yet, yes, or the window (or its late-entry cutoff) has passed. */
export type MyExamState = 'NotOpen' | 'Open' | 'Closed';

/** One of a candidate's attempts at an exam, as listed. All instants are UTC. */
export interface AttemptSummaryDto {
  id: string;
  /** Which attempt this is for them at the exam, from 1. */
  number: number;
  status: AttemptStatus;
  startedAtUtc: string;
  submittedAtUtc: string | null;
  /** Whether it ended because time ran out rather than because the candidate submitted it. */
  autoSubmitted: boolean;
  /** The marks scored and available, once submitted. */
  score: number | null;
  maxScore: number | null;
}

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
  /** The candidate's latest attempt at this exam, or null if they have not started it. */
  attemptId: string | null;
  attemptStatus: AttemptStatus | null;
  /** The marks the latest attempt scored and the marks available, once it is submitted. */
  score: number | null;
  maxScore: number | null;
  /** How many attempts they may make in all: one, plus each extra attempt an administrator gave them. */
  attemptsAllowed: number;
  attemptsUsed: number;
  /** Whether they may start a new attempt now: the window is open, none is in progress, and one is left. */
  canStartAttempt: boolean;
  /** Every attempt they have made, oldest first. */
  attempts: AttemptSummaryDto[];
}

/** When candidates may see which of their answers were right, as the exam's author chose it. */
export type ReviewReleaseMode = 'Instant' | 'Scheduled' | 'Manual';

/** Whether a submitted attempt's answers can be reviewed, and if not, when they will be able to. */
export interface AttemptReviewAvailability {
  available: boolean;
  mode: ReviewReleaseMode;
  /** From when the review opens, when that is already decided (Scheduled); null otherwise. UTC. */
  availableFromUtc: string | null;
}

/** An answer option as the candidate sees it while sitting the exam. The API never says which one is correct. */
export interface AttemptOptionDto {
  id: string;
  text: string;
}

/** A question as the candidate sees it, with the option they have saved so far and whether they marked it to come back to. */
export interface AttemptQuestionDto {
  id: string;
  text: string;
  options: AttemptOptionDto[];
  selectedOptionId: string | null;
  /** A note to themselves only: it never affects the score. */
  markedForReview: boolean;
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
  /** Whether the answers can be reviewed; null (or absent) while the attempt is still open. */
  review?: AttemptReviewAvailability | null;
  /** Which attempt this is for the candidate at the exam, from 1. */
  number?: number;
  /** Whether sections are locked: once the candidate moves past a section they cannot return to it. */
  sectionLockEnabled?: boolean;
  /** The section the candidate is in, while the attempt is open and sections are locked. */
  activeSectionId?: string | null;
}

/** How one question was marked. */
export type AnswerVerdict = 'Correct' | 'Wrong' | 'Unanswered';

/** An option in a review: whether it is the right one, and whether the candidate chose it. */
export interface ReviewOptionDto {
  id: string;
  text: string;
  isCorrect: boolean;
  wasChosen: boolean;
}

/** A question in a review, with how it was marked. `text` is sanitized HTML. */
export interface ReviewQuestionDto {
  id: string;
  text: string;
  options: ReviewOptionDto[];
  verdict: AnswerVerdict;
  /** The marks this question earned; may be negative. */
  marks: number;
}

export interface ReviewSectionDto {
  id: string;
  name: string;
  questions: ReviewQuestionDto[];
}

/** A submitted attempt with its answer key; only ever sent once the attempt is over and the answers are released. */
export interface AttemptReviewDto {
  attemptId: string;
  examId: string;
  examName: string;
  /** Which attempt this is for the candidate at the exam, from 1. */
  number: number;
  submittedAtUtc: string | null;
  autoSubmitted: boolean;
  score: number;
  maxScore: number;
  correctCount: number;
  wrongCount: number;
  unansweredCount: number;
  sections: ReviewSectionDto[];
}
