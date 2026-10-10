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
  /** The marks scored and available, once submitted; null for the candidate once an administrator invalidated the result. */
  score: number | null;
  maxScore: number | null;
  /** Staff only: how many times the candidate left the exam page (FR-22) and how many warnings were sent (FR-29). */
  focusViolations?: number | null;
  warnings?: number | null;
  /** Staff only (FR-26): how many times the attempt was seen from a different address or device than before, and on how many different devices. */
  clientChanges?: number | null;
  devices?: number | null;
  /** Whether an administrator has paused the attempt (FR-29). */
  paused?: boolean;
  /** Whether an administrator ended the attempt early, and why. */
  terminatedByAdmin?: boolean;
  terminationReason?: string | null;
  /** Whether an administrator invalidated the result, and why. */
  invalidated?: boolean;
  invalidationReason?: string | null;
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
  /** Whether the exam page turns off copying, pasting, right-click and printing (FR-23). Absent from an older API: on. */
  contentProtection?: boolean;
  /** How many times a candidate may leave the exam page before the attempt is ended (FR-22); 0 or absent when the exam does not watch. */
  focusViolationLimit?: number;
  /** What the candidate is told about what is turned off, recorded and watched, written by the server from the exam's proctoring settings (FR-46). */
  proctoringNotice?: string[];
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
  /** What they are allowed at this exam because of a disability or another need (FR-49); null or absent when nothing is. Never the staff note. */
  accommodation?: CandidateAccommodation | null;
}

/** An alternate format of the exam page an accommodation can give (FR-49). */
export type AccommodationFormat = 'large_text' | 'high_contrast' | 'screen_reader';

/** What a candidate is allowed because of a disability or another need (FR-49). The staff note is never sent to the candidate. */
export interface CandidateAccommodation {
  /** Seconds added to their deadline, already part of it once an attempt has started; 0 for none. */
  extraTimeSeconds: number;
  /** Whether they may use a reader or scribe. */
  readerScribe: boolean;
  /** The formats the exam page starts in; "screen_reader" also means leaving the page is not counted against them. */
  alternateFormats: AccommodationFormat[];
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
  /**
   * Whether the candidate types the answer instead of choosing options. Such a question has no options, and `answerText` is its answer.
   * Absent in a response from before text questions existed, which means false.
   */
  isTextAnswer?: boolean;
  /** What the candidate typed for a text question, or null when they have not typed anything. */
  answerText?: string | null;
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
  /**
   * Whether the exam page turns off copying, pasting, right-click and printing while the attempt is open (FR-23). Absent from an
   * older API, which is read as on, since protection is the default.
   */
  contentProtection?: boolean;
  /** How many times the candidate may leave the exam page before the server ends the attempt (FR-22); 0 or absent when the exam does not watch. */
  focusViolationLimit?: number;
  /** How many times they have left it so far in this attempt. */
  focusViolations?: number;
  /** Whether the server ended the attempt because the limit was reached. */
  endedByViolations?: boolean;
  /** When an administrator paused the attempt (FR-29); null or absent while it runs. */
  pausedAtUtc?: string | null;
  /** Every warning administrators sent during the attempt, oldest first. */
  warnings?: AttemptWarningDto[];
  /** Whether an administrator ended the attempt early, and why. */
  terminatedByAdmin?: boolean;
  terminationReason?: string | null;
  /** Whether an administrator invalidated the result: there is then no score or review, and this says why. */
  invalidated?: boolean;
  invalidationReason?: string | null;
  /** What this attempt carries because of the candidate's accommodation (FR-49); null or absent when none applies. */
  accommodation?: CandidateAccommodation | null;
}

/** A warning an administrator sent the candidate during the attempt (FR-29). */
export interface AttemptWarningDto {
  id: string;
  message: string;
  issuedAtUtc: string;
}

/** What the exam page asks for every few seconds while an attempt is open: its state, deadline and warnings, without the questions. */
export interface AttemptStatusDto {
  status: AttemptStatus;
  pausedAtUtc: string | null;
  /** When the server will close the attempt; later than before once a pause is resumed. */
  deadlineUtc: string;
  serverTimeUtc: string;
  warnings: AttemptWarningDto[];
}

/** How a candidate left the exam page (FR-22); the names the API accepts. */
export type FocusViolationKind = 'TabHidden' | 'WindowBlurred' | 'FullscreenExited';

/** What the API answers after a departure is reported. */
export interface FocusViolationResultDto {
  /** How many times the candidate has now left the page in this attempt. */
  violations: number;
  /** How many times they may; 0 when the exam does not watch, in which case nothing was recorded. */
  limit: number;
  /** Whether this departure reached the limit, so the server has ended and scored the attempt. */
  attemptEnded: boolean;
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
  /** Whether the candidate typed the answer. Such a question has no options; absent means false. */
  isTextAnswer?: boolean;
  /** What the candidate typed for a text question; null when they answered by choosing options or did not answer. */
  answerText?: string | null;
  /** The answers that were accepted for a text question, so the candidate can see what counted as right. */
  acceptedAnswers?: string[];
  /** Why the correct answer is correct, as plain text; null or absent when none was written. Only in a released review (FR-33). */
  explanation?: string | null;
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
  /** The result version this revision produced; the first revision makes version 2 (FR-31). Absent from an API that predates result versions. */
  version?: number;
}

/** What kind of problem a candidate reports from inside an exam (FR-42). */
export type IssueCategory = 'Question' | 'Technical' | 'Other';

/** The kinds a candidate can choose from, in the order they are offered. */
export const ISSUE_CATEGORIES: readonly IssueCategory[] = ['Question', 'Technical', 'Other'];

/** The longest description the API accepts on a reported problem. */
export const MAX_ISSUE_MESSAGE = 1000;

/** A problem the candidate has just reported from inside the exam (FR-42); it only confirms what was recorded. */
export interface MyIssueReportDto {
  id: string;
  category: IssueCategory;
  /** The question on screen when it was reported, if it was about one. */
  questionId: string | null;
  message: string;
  reportedAtUtc: string;
}

/** Where a candidate's dispute of an answer key stands (FR-31). */
export type DisputeStatus = 'Open' | 'Accepted' | 'Rejected';

/** The longest reason the API accepts on a dispute. */
export const MAX_DISPUTE_REASON = 1000;

/** The candidate's own dispute of one question's answer key (FR-31); a question can be disputed once per attempt. */
export interface MyDisputeDto {
  id: string;
  questionId: string;
  /** Why the candidate thinks the answer key is wrong. */
  reason: string;
  raisedAtUtc: string;
  status: DisputeStatus;
  resolvedAtUtc: string | null;
  /** Once answered: what staff said when rejecting, or the reason the answer key was corrected when accepting. */
  resolutionNote: string | null;
}

/** Whether the answer key of a result can still be disputed (FR-31). The time is counted from when the result was released. */
export interface DisputeWindowDto {
  /** Whether disputes are being taken at all. */
  enabled: boolean;
  /** Whether one can be raised now. */
  open: boolean;
  /** When the time to dispute ends; null when there is no release time to count from. UTC. */
  closesAtUtc: string | null;
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
  /** Which version of the result the score above is: 1 as first submitted, one more for each revision (FR-31). Absent from an API that predates result versions, which means 1. */
  resultVersion?: number;
  /** Whether the answer key can be disputed, and until when (FR-31); null or absent when the API does not say, and then the page says nothing about disputes. */
  disputeWindow?: DisputeWindowDto | null;
  /** The candidate's own disputes of this attempt's answer keys, oldest first; null or absent when none are known. */
  disputes?: MyDisputeDto[] | null;
}

/** One section of a result: the marks it earned and how its questions were answered (FR-32). Carries no answers. */
export interface SectionResultDto {
  id: string;
  name: string;
  /** The marks the section's questions earned; may be negative. */
  score: number;
  correctCount: number;
  wrongCount: number;
  partialCount: number;
  unansweredCount: number;
}

/**
 * A submitted attempt's result (FR-32): the score, where it stands among the exam's released results, and the marks by section. The API
 * answers only once the exam's author has released the results.
 */
export interface AttemptResultDto {
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
  partialCount: number;
  unansweredCount: number;
  sections: SectionResultDto[];
  /** The place among the exam's released results; 1 is the best, and ties share a place. */
  rank: number;
  /** The share of the results compared that scored the same or less, from 0 to 100, rounded down to two decimals. */
  percentile: number;
  /** How many results the rank and percentile were worked out from, this one included. */
  cohortSize: number;
  /** Whether the exam is still open, so the rank may still move. */
  provisional: boolean;
  /** Which version of the result the score is: 1 as first submitted, one more for each revision (FR-31). */
  resultVersion: number;
}
