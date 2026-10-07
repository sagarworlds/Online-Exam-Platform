import { Signal, computed, signal } from '@angular/core';
import { BookDto } from './book.models';

/** The value of a class select that means "books of every class"; also the empty filter. */
export const ANY_CLASS = '';

/** The value of a class select that means "books that are not filed under a class". Class ids are GUIDs, so it cannot clash. */
export const NO_CLASS = 'none';

/** A class as a select offers it. */
export interface ClassChoice {
  id: string;
  name: string;
}

/** Natural order, so "2nd" comes before "10th". */
export function compareNames(a: string, b: string): number {
  return a.localeCompare(b, undefined, { numeric: true });
}

/** The select value of the class a book is filed under. */
export function classKeyOf(book: Pick<BookDto, 'classId'>): string {
  return book.classId ?? NO_CLASS;
}

/** The distinct classes the books are filed under, in natural order of name. A class with no book among them is not listed. */
export function classChoicesOf(books: readonly Pick<BookDto, 'classId' | 'className'>[]): ClassChoice[] {
  const found = new Map<string, ClassChoice>();
  for (const book of books) {
    if (book.classId && !found.has(book.classId)) {
      found.set(book.classId, { id: book.classId, name: book.className ?? '' });
    }
  }

  return [...found.values()].sort((a, b) => compareNames(a.name, b.name));
}

/** The books of one class (ANY_CLASS for every book, those of a class first and those without one last), keeping their order otherwise. */
export function booksInClass<T extends Pick<BookDto, 'classId' | 'className'>>(books: readonly T[], key: string): T[] {
  if (key !== ANY_CLASS) {
    return books.filter((book) => classKeyOf(book) === key);
  }

  const withClass = books.filter((book) => book.classId).sort((a, b) => compareNames(a.className ?? '', b.className ?? ''));
  return [...withClass, ...books.filter((book) => !book.classId)];
}

/** What a book is called in a select: "4th · English" while books of every class are listed, plain "English" once a class is chosen. */
export function bookOptionLabel(book: Pick<BookDto, 'name' | 'className'>, key: string): string {
  return key === ANY_CLASS && book.className ? `${book.className} · ${book.name}` : book.name;
}

/**
 * The Class step in front of a Book select: which class the select shows, which books it leaves to choose from, and what to
 * do when the class changes. It serves both places a book is chosen (the question picker and the exam scope), which then
 * only add what is theirs, such as clearing the chapter.
 *
 * The class is not stored by whatever holds the book choice, which carries a book id only. It follows the chosen book (so a
 * question already filed shows its class) until the person picks a class or a book here, and then stays as they left it.
 * A book of another class, set from outside, still wins over a class that does not hold it, so the two never disagree.
 */
export class ClassCascade {
  /** What the person chose on the class select, or null while the class simply follows the chosen book. */
  private readonly chosen = signal<string | null>(null);

  /** Classes that have an open book, in natural order. */
  readonly classes: Signal<ClassChoice[]>;
  /** Whether the class select is worth showing: with no book under any class it would only say "Any class". */
  readonly offered: Signal<boolean>;
  /** Whether some open book has no class, so "No class" leads somewhere. */
  readonly offersNoClass: Signal<boolean>;
  /** The select value to show: ANY_CLASS, NO_CLASS or a class id. */
  readonly key: Signal<string>;
  /** The open books to choose from under the class shown. */
  readonly books: Signal<BookDto[]>;

  constructor(
    private readonly openBooks: Signal<readonly BookDto[]>,
    boundBookId: Signal<string>,
  ) {
    this.classes = computed(() => classChoicesOf(openBooks()));
    this.offered = computed(() => this.classes().length > 0);
    this.offersNoClass = computed(() => openBooks().some((book) => !book.classId));

    const boundKey = computed(() => {
      const bound = openBooks().find((book) => book.id === boundBookId());
      return bound ? classKeyOf(bound) : null;
    });
    this.key = computed(() => {
      if (!this.offered()) {
        return ANY_CLASS;
      }

      const chosen = this.chosen();
      const bound = boundKey();
      const key = bound !== null && (chosen === null || (chosen !== ANY_CLASS && chosen !== bound)) ? bound : (chosen ?? ANY_CLASS);
      const available = key === ANY_CLASS || (key === NO_CLASS ? this.offersNoClass() : this.classes().some((c) => c.id === key));
      return available ? key : ANY_CLASS;
    });
    this.books = computed(() => booksInClass(openBooks(), this.key()));
  }

  /** Whether a book may stay chosen once the class select shows `key`: "any class" holds every book, a class only its own. */
  fits(bookId: string, key: string): boolean {
    if (!bookId || key === ANY_CLASS) {
      return true;
    }

    const book = this.openBooks().find((candidate) => candidate.id === bookId);
    return book !== undefined && classKeyOf(book) === key;
  }

  /** The person picked a class. The caller drops a book that does not {@link fits} it first. */
  choose(key: string): void {
    this.chosen.set(key);
  }

  /** The person picked a book: the class stays as shown (so "Any class" is not turned into the book's class behind their back). */
  keep(): void {
    this.chosen.update((chosen) => chosen ?? this.key());
  }
}
