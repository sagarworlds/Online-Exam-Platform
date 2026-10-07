/** A class (such as "4th") that books are filed under: the level above a book in Class, Book, Chapter, Question. */
export interface ClassDto {
  id: string;
  name: string;
  /** An archived class keeps its books but takes no new ones and is hidden from pickers. */
  isArchived: boolean;
  /** How many books are filed under the class. */
  bookCount: number;
  createdAtUtc: string;
}

/** The body of POST /v1/classes and PUT /v1/classes/{id}. */
export interface ClassRequest {
  name: string;
}

/** The API's limits on a class, mirrored here so the form can refuse early. */
export const CLASS_LIMITS = { name: 100 } as const;
