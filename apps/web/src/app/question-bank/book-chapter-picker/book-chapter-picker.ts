import { Component, computed, input, model } from '@angular/core';
import { RouterLink } from '@angular/router';
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
 * Chooses a book and then one of its chapters. Used wherever a question is filed: the new-question form, a single
 * question's "File under" row and the bulk toolbar. Archived books and chapters are not offered: they keep what they
 * have but take nothing new.
 */
@Component({
  selector: 'app-book-chapter-picker',
  imports: [RouterLink],
  templateUrl: './book-chapter-picker.html',
})
export class BookChapterPicker {
  /** Every book; archived ones are filtered out here. */
  readonly books = input.required<readonly BookDto[]>();
  /** Prefix of the two selects' ids (`<prefix>-book`, `<prefix>-chapter`), so several pickers can share a page. */
  readonly idPrefix = input.required<string>();
  readonly bookLabel = input('Book (optional)');
  /** The text of the choice that means "no book". */
  readonly noBookLabel = input('Not filed under a book');
  /** The choice, two-way bound so the page always has the latest. */
  readonly value = model<Placement>(NO_PLACEMENT);

  protected readonly openBooks = computed(() => this.books().filter((book) => !book.isArchived));
  protected readonly openChapters = computed(
    () => this.books().find((book) => book.id === this.value().bookId)?.chapters.filter((chapter) => !chapter.isArchived) ?? [],
  );

  protected chooseBook(bookId: string): void {
    // A chapter belongs to one book, so changing the book drops the old chapter rather than keep a mismatch.
    this.value.set({ bookId, chapterId: '' });
  }

  protected chooseChapter(chapterId: string): void {
    this.value.update((current) => ({ ...current, chapterId }));
  }
}
