/** An exam's lifecycle: only a Draft can be edited, only a Published exam can be taken. */
export type ExamStatus = 'Draft' | 'Published' | 'Archived';

/** Marks awarded per answer; the API's default is +1 / 0 / 0. */
export interface MarkingSchemeDto {
  correctMarks: number;
  incorrectMarks: number;
  unattemptedMarks: number;
}

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
  markingScheme: MarkingSchemeDto;
}

/** A question as it sits in an exam; `text` comes from the question bank and is null if the bank lost it. */
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

/** The body of PUT /v1/exams/{id}/schedule. Every instant is UTC (ISO 8601). */
export interface ScheduleExamRequest {
  scheduledStartTime: string;
  scheduledEndTime: string;
  timeZone?: string;
  lateEntryDeadline?: string | null;
  /** Minutes one attempt lasts; omit for "the whole window". */
  durationMinutes?: number | null;
}
