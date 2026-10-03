import { QuestionDifficulty } from '../question-bank/question.models';

/** An exam's lifecycle: only a Draft can be edited, only a Published exam can be taken. */
export type ExamStatus = 'Draft' | 'Published' | 'Archived';

/** Marks awarded per answer; the API's default is +1 / 0 / 0. */
export interface MarkingSchemeDto {
  correctMarks: number;
  incorrectMarks: number;
  unattemptedMarks: number;
  /** Whether a multiple-answer question that is partly right earns a share of the marks. Absent means off. */
  partialCredit?: boolean;
}

/** When candidates may see which of their answers were right: right after submitting, from a set time, or when released by hand. */
export type ResultReleaseMode = 'Instant' | 'Scheduled' | 'Manual';

export interface ExamConfigDto {
  /** How long one attempt lasts; null means "until the window closes". */
  totalTimeSeconds: number | null;
  shuffleQuestions: boolean;
  shuffleOptions: boolean;
  sectionLockEnabled: boolean;
  calculatorAllowed: boolean;
  scratchpadAllowed: boolean;
  maxAttempts: number;
  maxRetakes: number;
  resultReleaseMode: ResultReleaseMode;
  /** From when the answers are visible: set for Scheduled, and for Manual once released; null otherwise. UTC. */
  resultReleaseTime: string | null;
  markingScheme: MarkingSchemeDto;
}

/** The body of PUT /v1/exams/{id}/result-release. */
export interface ResultReleaseRequest {
  mode: ResultReleaseMode;
  /** UTC instant; required for Scheduled, ignored otherwise. */
  releaseTime?: string | null;
}

/** The body of PUT /v1/exams/{id}/shuffle; draft exams only. Both choices are always sent. */
export interface ShuffleRequest {
  shuffleQuestions: boolean;
  shuffleOptions: boolean;
}

/** The body of PUT /v1/exams/{id}/marking-scheme; draft exams only. A correct answer earns more than 0, the others 0 or less. */
export type MarkingSchemeRequest = MarkingSchemeDto;

/** The body of PUT /v1/exams/{id}/attempt-limit: how many attempts every enrolled candidate has, from 1 to 10. */
export interface AttemptLimitRequest {
  maxAttempts: number;
}

/** A question as it sits in an exam; `text` comes from the question bank and is null if the bank lost it. */
/** The body of POST .../sections/{id}/questions/draw: how many random bank questions to add, and which ones are eligible. */
export interface DrawQuestionsRequest {
  /** How many to draw, 1 to {@link MAX_DRAW_COUNT}. */
  count: number;
  /** Only questions of this difficulty; null for any. */
  difficulty: QuestionDifficulty | null;
  /** Only questions with this topic; null for any. */
  topic: string | null;
}

/** The most questions one draw may add; the API refuses more. */
export const MAX_DRAW_COUNT = 100;

export interface ExamQuestionDto {
  id: string;
  questionId: string;
  order: number;
  text: string | null;
}

export interface ExamSectionDto {
  id: string;
  name: string;
  timeSeconds: number | null;
  order: number;
  questions: ExamQuestionDto[];
  /** Rules that add questions drawn afresh for each candidate when they start; absent from older responses. */
  drawRules?: DrawRuleDto[];
}

/** A rule that draws random bank questions for each candidate who starts an attempt. */
export interface DrawRuleDto {
  id: string;
  order: number;
  count: number;
  bookId: string | null;
  chapterId: string | null;
  difficulty: string | null;
  topic: string | null;
}

/** What an exam's questions may be drawn from: anywhere in the bank, one whole book, or chosen chapters of one book. */
export type ExamScopeType = 'Independent' | 'Book' | 'Chapters';

/** A chosen chapter of an exam's scope; `title` is null if the question bank no longer has it. */
export interface ExamScopeChapterDto {
  id: string;
  title: string | null;
}

/** An exam's scope with the names to show. `bookName` is null for an independent exam, or if the bank lost the book. */
export interface ExamScopeDto {
  type: ExamScopeType;
  bookId: string | null;
  bookName: string | null;
  /** The chosen chapters for a `Chapters` scope; empty for the others. */
  chapters: ExamScopeChapterDto[];
}

/** The body of PUT /v1/exams/{id}/scope, and the optional `scope` of a new exam. */
export interface ExamScopeRequest {
  type: ExamScopeType;
  bookId?: string | null;
  chapterIds?: string[] | null;
}

/** An exam as the authoring side sees it. `sections` is filled in when one exam is read, null in a listing. */
export interface ExamDto {
  id: string;
  seriesId: string | null;
  name: string;
  description: string | null;
  status: ExamStatus;
  config: ExamConfigDto;
  /** UTC instants. A new exam has not been scheduled yet; check `isScheduled`, not these dates. */
  scheduledStartTime: string;
  scheduledEndTime: string;
  lateEntryDeadline: string | null;
  timeZone: string;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  isScheduled: boolean;
  sections: ExamSectionDto[] | null;
  /** What the exam's questions may be drawn from. */
  scope: ExamScopeDto;
}

export interface CreateExamRequest {
  /** Null (or omitted) creates a standalone exam; the API rejects an empty string. */
  seriesId?: string | null;
  name: string;
  description?: string;
  /** Omit for an exam whose questions may come from anywhere in the bank. */
  scope?: ExamScopeRequest;
}

/** The body of PUT /v1/exams/{id}/details: the name and description candidates see. A blank description clears it. */
export interface UpdateExamDetailsRequest {
  name: string;
  description: string | null;
}

/**
 * The body of PUT /v1/exams/{id}/sections/{sectionId}. It replaces both values, so `timeSeconds` must carry the section's
 * current limit (or null for none) unless the author means to change it; leaving it out would clear a limit.
 */
export interface EditSectionRequest {
  name: string;
  timeSeconds: number | null;
}

/** The body of PUT /v1/exams/{id}/schedule. Every instant is UTC (ISO 8601). */
export interface ScheduleExamRequest {
  scheduledStartTime: string;
  scheduledEndTime: string;
  timeZone?: string;
  lateEntryDeadline?: string | null;
  /** Minutes one attempt lasts; omit for "the whole window". */
  durationMinutes?: number | null;
}
