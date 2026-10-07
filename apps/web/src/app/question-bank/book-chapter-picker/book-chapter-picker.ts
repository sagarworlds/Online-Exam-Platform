import { Component, computed, input, model } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ANY_CLASS, ClassCascade, NO_CLASS, bookOptionLabel } from '../../book-management/book-class';
import { BookDto } from '../../book-management/book.models';

/** A book and a chapter chosen together; both are '' while nothing is chosen. */
export interface Placement {
  bookId: string;
  chapterId: string;
}

export const NO_PLACEMENT: Placement = { bookId: '', chapterId: '' };

/** Whether a placement can be used: either nothing is chosen, or a chapter is, because a question belongs to a chapter, not a book. */
export function isCompletePlacement(placement: Placement): boolean {
  return !placement.bookId || !!placement.chapterId;
}

/**
 * Chooses a class, then one of its books, then one of the book's chapters. Used wherever a question is filed: the new-question
 * form, a single question's "File under" row and the bulk toolbar. Archived books and chapters are not offered: they keep what
 * they have but take nothing new.
 *
 * The class is only a way to narrow the books, so the choice stays a book and a chapter: the classes are worked out from the
 * books, and the class select is not shown while no book has one. "Any class" lists the books of every class, each labelled
 * with its class.
 */
@Component({
  selector: 'app-book-chapter-picker',
  imports: [RouterLink],
  templateUrl: './book-chapter-picker.html',
})
export class BookChapterPicker {
  /** Every book; archived ones are filtered out here. */
  readonly books = input.required<readonly BookDto[]>();
  /** Prefix of the selects' ids (`<prefix>-class`, `<prefix>-book`, `<prefix>-chapter`), so several pickers can share a page. */
  readonly idPrefix = input.required<string>();
  readonly bookLabel = input('Book (optional)');
  /** The text of the choice that means "no book". */
  readonly noBookLabel = input('Not filed under a book');
  /** The choice, two-way bound so the page always has the latest. */
  readonly value = model<Placement>(NO_PLACEMENT);

  protected readonly openBooks = computed(() => this.books().filter((book) => !book.isArchived));
  /** The class step in front of the book select; it follows a book chosen from outside, such as one a question is filed under. */
  protected readonly cascade = new ClassCascade(
    this.openBooks,
    computed(() => this.value().bookId),
  );
  protected readonly anyClass = ANY_CLASS;
  protected readonly noClass = NO_CLASS;
  protected readonly optionLabel = bookOptionLabel;
  protected readonly openChapters = computed(
    () => this.books().find((book) => book.id === this.value().bookId)?.chapters.filter((chapter) => !chapter.isArchived) ?? [],
  );

  protected chooseClass(key: string): void {
    // A book belongs to one class, so one that is not in the new class is dropped, with its chapter.
    if (!this.cascade.fits(this.value().bookId, key)) {
      this.value.set(NO_PLACEMENT);
    }
    this.cascade.choose(key);
  }

  protected chooseBook(bookId: string): void {
    this.cascade.keep();
    // A chapter belongs to one book, so changing the book drops the old chapter rather than keep a mismatch.
    this.value.set({ bookId, chapterId: '' });
  }

  protected chooseChapter(chapterId: string): void {
    this.value.update((current) => ({ ...current, chapterId }));
  }
}
