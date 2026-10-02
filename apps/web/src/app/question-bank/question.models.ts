/** One answer option of a question, as the authoring side sees it. */
export interface QuestionOptionDto {
  id: string;
  text: string;
  isCorrect: boolean;
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
}

/** The body of POST /v1/questions. The author is the caller, so it carries no user id. */
export interface CreateQuestionRequest {
  /** The question as HTML from the editor; the API sanitizes it before storing. */
  text: string;
  options: { text: string; isCorrect: boolean }[];
  /** The chapter to file the question under; null leaves it unfiled. */
  chapterId: string | null;
}

/** Narrows the question list. `unfiled` and a book or chapter are alternatives; a chapter implies its book. */
export interface QuestionFilter {
  bookId?: string;
  chapterId?: string;
  unfiled?: boolean;
}

/** The API's limits on one question, mirrored here so the form can refuse early. */
export const QUESTION_LIMITS = { minOptions: 2, maxOptions: 6 } as const;
