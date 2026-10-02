/** One chapter of a book, as the authoring side sees it (FR-5). */
export interface ChapterDto {
  id: string;
  bookId: string;
  title: string;
  /** Position within the book, from 1, in the order the chapters were added. */
  order: number;
  /** An archived chapter keeps its questions but takes no new ones and is hidden from pickers. */
  isArchived: boolean;
  /** How many questions are filed under the chapter. */
  questionCount: number;
}

/** A book with its chapters: the unit authors organise questions by. */
export interface BookDto {
  id: string;
  name: string;
  subject: string | null;
  description: string | null;
  /** An archived book keeps its questions but takes no new chapters and is hidden from pickers. */
  isArchived: boolean;
  chapters: ChapterDto[];
  createdBy: string;
  createdAtUtc: string;
}

/** The body of POST /v1/books and PUT /v1/books/{id}. */
export interface BookRequest {
  name: string;
  subject: string | null;
  description: string | null;
}

/** The API's limits on a book, mirrored here so the form can refuse early. */
export const BOOK_LIMITS = { name: 200, subject: 100, description: 1000, chapterTitle: 200 } as const;
