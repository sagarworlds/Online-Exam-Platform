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

/** How hard an author judges a question to be; a label for finding questions, never part of a score. */
export type QuestionDifficulty = 'easy' | 'medium' | 'hard';

/** The difficulties in the order a picker lists them, with the words an author reads. */
export const QUESTION_DIFFICULTIES: readonly { value: QuestionDifficulty; label: string }[] = [
  { value: 'easy', label: 'Easy' },
  { value: 'medium', label: 'Medium' },
  { value: 'hard', label: 'Hard' },
];

/** A language a question can be written in (FR-10). */
export type QuestionLanguage = 'en' | 'hi' | 'mr';

/** The languages in the order a picker lists them, with the words an author reads. */
export const QUESTION_LANGUAGES: readonly { code: QuestionLanguage; label: string }[] = [
  { code: 'en', label: 'English' },
  { code: 'hi', label: 'Hindi' },
  { code: 'mr', label: 'Marathi' },
];

/** One question in a group of translations (FR-10). */
export interface QuestionTranslation {
  id: string;
  language: QuestionLanguage;
  /** The start of its wording as plain text. */
  preview: string;
  status: QuestionStatus;
}

/** The body of POST /v1/questions/{id}/translations: words only, as the answer key is copied from the question translated. */
export interface AddTranslationRequest {
  language: QuestionLanguage;
  /** The translated question as HTML from the editor. */
  text: string;
  /** One text for each option of the question translated, in the same order. */
  options: string[];
}

/** A question with its answer key (FR-5). Only authors get this shape; candidates never see `isCorrect`. */
export interface QuestionDto {
  id: string;
  /** The question as sanitized HTML. Show it with `[innerHTML]`; use `htmlToPlainText` where markup cannot render. */
  text: string;
  options: QuestionOptionDto[];
  createdBy: string;
  createdAtUtc: string;
  /** Where the question is filed; all six are null for a question that is not filed under a chapter. A book without a class has null for the class. */
  chapterId: string | null;
  chapterTitle: string | null;
  bookId: string | null;
  bookName: string | null;
  classId: string | null;
  className: string | null;
  usage: QuestionUsageDto;
  /** Null when the author has not said how hard the question is. */
  difficulty: QuestionDifficulty | null;
  /** Free-text topics in lower case, such as "fractions"; at most {@link QUESTION_LIMITS}.maxTopics. */
  topics: string[];
  /** Where the question is in the review workflow (FR-8); absent from an API that predates it. */
  status?: QuestionStatus;
  /** Whether more than one option may be correct; a candidate must then choose exactly the correct ones to be marked right. */
  allowsMultiple: boolean;
  /** The language it is written in; absent from an API that predates languages (FR-10). */
  language?: QuestionLanguage;
  /** Whether the candidate types the answer instead of choosing options (a text question); absent from an API that predates it. */
  isTextAnswer?: boolean;
  /** The answers a typed answer may be, as the author wrote them; empty for a multiple-choice question. Absent from an API that predates it. */
  acceptedAnswers?: string[];
  /** Why the correct answer is correct, as plain text; a candidate sees it only in the answer review, once released (FR-33). Absent or null means none. */
  explanation?: string | null;
}

/** The body of POST /v1/questions. The author is the caller, so it carries no user id. */
export interface CreateQuestionRequest {
  /** The question as HTML from the editor; the API sanitizes it before storing. */
  text: string;
  /** Empty for a text question, which has no options. */
  options: { text: string; isCorrect: boolean; isPinned: boolean }[];
  /** The chapter to file the question under; null leaves it unfiled. */
  chapterId: string | null;
  difficulty: QuestionDifficulty | null;
  topics: string[];
  /** True when more than one option is correct. */
  allowsMultiple: boolean;
  /** True to add the question although the bank already has one with the same wording and options (FR-9). */
  allowDuplicate?: boolean;
  /** The language the question is written in; the same question in another language is added as a translation (FR-10). */
  language?: QuestionLanguage;
  /** True for a text question, which the candidate answers by typing; its answer key is `acceptedAnswers`. */
  isTextAnswer?: boolean;
  /** The answers a typed answer may be, for a text question; empty for a multiple-choice one. */
  acceptedAnswers?: string[];
  /** Why the correct answer is correct, as plain text; a candidate sees it only in the answer review, once released (FR-33). Absent or null means none. */
  explanation?: string | null;
}

/** A question already in the bank that repeats one being added (FR-9). */
export interface DuplicateQuestion {
  id: string;
  /** The start of its wording as plain text. */
  preview: string;
  /** Whether its options match too; false means only the wording does. */
  sameOptions: boolean;
  status: string;
  chapterId: string | null;
}

/** How often one option was chosen (FR-9). */
export interface OptionStatistics {
  id: string;
  text: string;
  isCorrect: boolean;
  timesChosen: number;
}

/** Where a question is used and how candidates fared on it (FR-9). */
export interface QuestionStatistics {
  examCount: number;
  examNames: string[];
  /** In how many finished, valid attempts it was answered. */
  answered: number;
  /** How many of those answers chose exactly the correct options. */
  correct: number;
  /** 0 to 100, or null while nobody has answered it. */
  percentCorrect: number | null;
  options: OptionStatistics[];
}

/** The body of PUT /v1/questions/{id}: the question's whole new content, not a patch. */
export interface UpdateQuestionRequest {
  text: string;
  /** All the options after the edit, in display order. An option the question already has keeps its `id`; a new one has none. */
  options: { id: string | null; text: string; isCorrect: boolean; isPinned: boolean }[];
  /** Like everything else here these replace what was there, so an edit that omits them clears the labels. */
  difficulty: QuestionDifficulty | null;
  topics: string[];
  /** Like the options, this is part of the answer key: it cannot change once candidates have answered. */
  allowsMultiple: boolean;
  /** Like the options, whether the candidate types the answer is part of the answer key, so it cannot change once they have answered. */
  isTextAnswer?: boolean;
  /** The accepted answers of a text question, which are part of its answer key like the options of another. */
  acceptedAnswers?: string[];
  /** Why the correct answer is correct, as plain text; a candidate sees it only in the answer review, once released (FR-33). Absent or null means none. */
  explanation?: string | null;
}

/** The longest reason the API accepts for correcting an answer key; every candidate whose score moves is shown it. */
export const MAX_KEY_CORRECTION_REASON = 500;

/** What correcting an answer key did (FR-31). */
export interface AnswerKeyCorrectionResult {
  /** Whether the key differed from what was asked for; false means nothing was changed or rescored. */
  keyChanged: boolean;
  /** How many submitted attempts were scored again because of it. */
  attemptsRescored: number;
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

/** Narrows the question list. `unfiled` and a book or chapter are alternatives; a chapter implies its book, and a book its class. */
export interface QuestionFilter {
  /** Combines with the place filters: only questions filed under a chapter of a book of this class. */
  classId?: string;
  bookId?: string;
  chapterId?: string;
  unfiled?: boolean;
  /** Combines with the place filters: only questions of this difficulty. */
  difficulty?: QuestionDifficulty;
  /** Combines with the others: only questions that carry this topic. */
  topic?: string;
  /** Combines with the others: only questions whose text, or an option's text, contains this; case does not matter. */
  search?: string;
  /** Combines with the others: only questions in this review status. */
  status?: QuestionStatus;
  /** Combines with the others: only questions written in this language (FR-10). */
  language?: QuestionLanguage;
}

/** Where a question is in the review workflow (FR-8). */
export type QuestionStatus = 'draft' | 'in_review' | 'approved' | 'retired';

/** One line of a question's review thread: a comment, or a step of the workflow with the comment that came with it. */
export interface ReviewEntry {
  id: string;
  kind: 'commented' | 'submitted' | 'approved' | 'changes_requested' | 'retired' | 'restored';
  byLabel: string;
  comment: string;
  versionNumber: number;
  statusAfter: QuestionStatus;
  createdAtUtc: string;
}

/** What a review step did: the status afterwards, and the thread entry that records it. */
export interface ReviewResult {
  status: QuestionStatus;
  entry: ReviewEntry;
}

/** A review step a question can take. */
export type ReviewStep = 'submit-for-review' | 'approve' | 'request-changes' | 'retire' | 'restore';

/** The file formats questions can be imported from and exported to. */
export type QuestionFileFormat = 'csv' | 'xlsx' | 'json';

/** What an import did: the rows it created and the rows it left out, each with the line it came from in the file. */
export interface ImportQuestionsResult {
  created: { row: number; id: string }[];
  rejected: { row: number; errors: string[] }[];
  /** Rows left out because the same question already exists; absent from a response that predates FR-9. */
  duplicates?: { row: number; reason: string }[];
}

/** A downloaded export, and how many matching questions did not fit the format and were left out. */
export interface ExportedFile {
  blob: Blob;
  fileName: string;
  skipped: number;
}

/** The API's limits on one question, mirrored here so the form can refuse early. */
export const QUESTION_LIMITS = {
  minOptions: 2,
  maxOptions: 6,
  maxTopics: 5,
  maxTopicLength: 40,
  maxAcceptedAnswers: 10,
  maxAcceptedAnswerLength: 200,
  maxExplanationLength: 2000,
} as const;

/** The kinds of question an author writes: the candidate chooses options, or types the answer (a text question). */
export type QuestionType = 'choice' | 'text';

/** The question types in the order the type picker lists them, with the words an author reads. Multiple choice is the default. */
export const QUESTION_TYPES: readonly { value: QuestionType; label: string }[] = [
  { value: 'choice', label: 'Multiple choice' },
  { value: 'text', label: 'Text answer' },
];

/** How many questions one listing returns at most, as the API sets it. A full page means there may be more to load. */
export const QUESTION_LIST_PAGE_SIZE = 200;

/** How much of the bank's stored content is encrypted at rest (#57): the values there are, and how many are still plaintext. */
export interface ContentEncryptionStatus {
  contentValues: number;
  plaintextValues: number;
  complete: boolean;
}
