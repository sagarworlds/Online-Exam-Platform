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

/** Where a request for another attempt stands. */
export type AttemptRequestStatus = 'Pending' | 'Approved' | 'Declined';

/** The candidate's own request for another attempt at an exam. */
export interface MyAttemptRequestDto {
  id: string;
  status: AttemptRequestStatus;
  message: string | null;
  requestedAtUtc: string;
  decidedAtUtc: string | null;
  /** What the administrator said when declining, if anything. */
  decisionNote: string | null;
}

/** The longest message or note the API accepts on a request. */
export const MAX_ATTEMPT_REQUEST_TEXT = 500;

/** The rules of an exam a candidate is told before starting it (FR-17). */
export interface ExamRulesDto {
  /** Marks for a correct answer. */
  correctMarks: number;
  /** Marks for a wrong answer; negative when wrong answers cost marks. */
  incorrectMarks: number;
  /** Marks for a question left unanswered. */
  unattemptedMarks: number;
  /** Whether a multiple-answer question can earn part of its marks. */
  partialCredit: boolean;
  /** Whether leaving a section is final. */
  sectionLock: boolean;
  sectionCount: number;
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
  /** How many attempts they may make in all: the exam's limit per candidate, plus each extra attempt an administrator gave them. */
  attemptsAllowed: number;
  attemptsUsed: number;
  /** Whether they may start a new attempt now: the window is open, none is in progress, and one is left. */
  canStartAttempt: boolean;
  /** Every attempt they have made, oldest first. */
  attempts: AttemptSummaryDto[];
  /** Whether they may ask for another attempt now: they have used every attempt they hold, none is open, and no request is waiting. */
  canRequestAttempt: boolean;
  /** Their latest request for another attempt at this exam, if they ever made one. */
  attemptRequest: MyAttemptRequestDto | null;
  /** The marking and navigation rules the instructions page states; null from an older API. */
  rules: ExamRulesDto | null;
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
  /** The option chosen; for a multiple-answer question, the first of those chosen. Null when unanswered. */
  selectedOptionId: string | null;
  /** A note to themselves only: it never affects the score. */
  markedForReview: boolean;
  /**
   * Whether more than one option may be correct, so the candidate chooses a set and is marked right only for exactly the correct
   * ones. Absent in a response from before multiple-answer questions existed, which means false.
   */
  allowsMultiple?: boolean;
  /** Every option chosen; empty when unanswered. Absent in an older response, where `selectedOptionId` is the whole answer. */
  selectedOptionIds?: string[];
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
export type AnswerVerdict = 'Correct' | 'Partial' | 'Wrong' | 'Unanswered';

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
  /** Whether the candidate had to choose exactly the correct options, of which there may be several. Absent means false. */
  allowsMultiple?: boolean;
}

export interface ReviewSectionDto {
  id: string;
  name: string;
  questions: ReviewQuestionDto[];
}

/** One change to this attempt's score since it was first submitted, most often an answer-key correction. */
export interface ScoreRevisionDto {
  previousScore: number;
  previousMaxScore: number;
  newScore: number;
  newMaxScore: number;
  reason: string;
  revisedAtUtc: string;
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
  /** Multiple-answer questions answered partly right, for an exam with partial credit. Absent means none. */
  partialCount?: number;
  sections: ReviewSectionDto[];
  /** How the score has changed since this attempt was first submitted, oldest first. Absent or empty means never revised. */
  revisions?: ScoreRevisionDto[];
}
