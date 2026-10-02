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
}

/** The body of POST /v1/questions. The author is the caller, so it carries no user id. */
export interface CreateQuestionRequest {
  /** The question as HTML from the editor; the API sanitizes it before storing. */
  text: string;
  options: { text: string; isCorrect: boolean }[];
}

/** The API's limits on one question, mirrored here so the form can refuse early. */
export const QUESTION_LIMITS = { minOptions: 2, maxOptions: 6 } as const;
