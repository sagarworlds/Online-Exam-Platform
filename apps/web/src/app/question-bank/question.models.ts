/** One answer option of a question, as the authoring side sees it. */
export interface QuestionOptionDto {
  id: string;
  text: string;
  isCorrect: boolean;
  /** Whether the option keeps its place when options are shuffled, such as a "none of the above" that must stay last. */
  isPinned: boolean;
}

/** Where a question is in use, which decides what an author may still do to it. */
export interface QuestionUsageDto {
  /** How many exams contain the question; such a question cannot be deleted. */
  examCount: number;
  /** The names of some of those exams, at most five; `examCount` is always the full number. */
  examNames: string[];
  /** Whether a candidate has answered it; its answer key and option list can then no longer change. */
  answered: boolean;
}

/** A question with its answer key (FR-5). Only authors get this shape; candidates never see `isCorrect`. */
export interface QuestionDto {
  id: string;
  /** The question as sanitized HTML. Show it with `[innerHTML]`; use `htmlToPlainText` where markup cannot render. */
  text: string;
  options: QuestionOptionDto[];
  createdBy: string;
  createdAtUtc: string;
  /** Where the question is filed; all four are null for a question that is not filed under a chapter. */
  chapterId: string | null;
  chapterTitle: string | null;
  bookId: string | null;
  bookName: string | null;
  usage: QuestionUsageDto;
}

/** The body of POST /v1/questions. The author is the caller, so it carries no user id. */
export interface CreateQuestionRequest {
  /** The question as HTML from the editor; the API sanitizes it before storing. */
  text: string;
  options: { text: string; isCorrect: boolean; isPinned: boolean }[];
  /** The chapter to file the question under; null leaves it unfiled. */
  chapterId: string | null;
}

/** The body of PUT /v1/questions/{id}: the question's whole new content, not a patch. */
export interface UpdateQuestionRequest {
  text: string;
  /** All the options after the edit, in display order. An option the question already has keeps its `id`; a new one has none. */
  options: { id: string | null; text: string; isCorrect: boolean; isPinned: boolean }[];
}

/** The body of POST /v1/questions/placement: one question is a bulk of one. */
export interface FileQuestionsRequest {
  questionIds: string[];
  chapterId: string;
}

/** What filing did. */
export interface FileQuestionsResult {
  /** How many questions changed place; one already in the chapter is not counted. */
  moved: number;
  chapterId: string;
  chapterTitle: string;
  bookId: string;
  bookName: string;
}

/** Narrows the question list. `unfiled` and a book or chapter are alternatives; a chapter implies its book. */
export interface QuestionFilter {
  bookId?: string;
  chapterId?: string;
  unfiled?: boolean;
}

/** The API's limits on one question, mirrored here so the form can refuse early. */
export const QUESTION_LIMITS = { minOptions: 2, maxOptions: 6 } as const;

/** How many questions one listing returns at most, as the API sets it. A full page means there may be more to load. */
export const QUESTION_LIST_PAGE_SIZE = 200;
