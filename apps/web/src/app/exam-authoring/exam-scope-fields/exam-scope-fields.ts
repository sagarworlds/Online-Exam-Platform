import { Component, computed, input, model } from '@angular/core';
import { ANY_CLASS, ClassCascade, NO_CLASS, bookOptionLabel } from '../../book-management/book-class';
import { BookDto } from '../../book-management/book.models';
import { ExamScopeType } from '../exam.models';
import { NO_SCOPE, ScopeSelection } from './exam-scope';

let nextId = 0;

/**
 * The "questions come from" choice of an exam (FR-11): anywhere in the bank, one whole book, or chosen chapters of a
 * book. It only collects the choice; the page that holds it decides when it is complete and what to do with it, and the
 * API checks it all again. Archived books and chapters are not offered: they are kept for what is filed under them,
 * not to be chosen for something new. A Class select in front of the Book select narrows the books, and is only there while some
 * book has a class; the choice that is emitted is still the type, a book and chapters.
 */
@Component({
  selector: 'app-exam-scope-fields',
  templateUrl: './exam-scope-fields.html',
})
export class ExamScopeFields {
  /** Every book the author may choose from. */
  readonly books = input.required<readonly BookDto[]>();
  readonly disabled = input(false);
  /**
   * Keeps the legend for screen readers but not for the eye, for a page whose own heading already says it. The group
   * still needs a name of its own, so it is hidden rather than removed.
   */
  readonly hideLegend = input(false);
  /** The choice, two-way bound so the page always has the latest. */
  readonly scope = model<ScopeSelection>(NO_SCOPE);

  protected readonly uid = `scope-${nextId++}`;
  protected readonly types: readonly { value: ExamScopeType; label: string }[] = [
    { value: 'Independent', label: 'Anywhere in the question bank' },
    { value: 'Book', label: 'One whole book' },
    { value: 'Chapters', label: 'Chosen chapters of a book' },
  ];

  protected readonly openBooks = computed(() => this.books().filter((book) => !book.isArchived));
  /** The class step in front of the book select; it follows a book chosen from outside, such as the scope an exam already has. */
  protected readonly cascade = new ClassCascade(
    this.openBooks,
    computed(() => this.scope().bookId),
  );
  protected readonly anyClass = ANY_CLASS;
  protected readonly noClass = NO_CLASS;
  protected readonly optionLabel = bookOptionLabel;
  protected readonly openChapters = computed(
    () => this.books().find((book) => book.id === this.scope().bookId)?.chapters.filter((chapter) => !chapter.isArchived) ?? [],
  );

  protected chooseType(type: ExamScopeType): void {
    // Chapters belong to one book and a limit-free exam has none, so what no longer applies is cleared rather than
    // left to be sent by accident.
    this.scope.update((current) => ({
      type,
      bookId: type === 'Independent' ? '' : current.bookId,
      chapterIds: type === 'Chapters' ? current.chapterIds : [],
    }));
  }

  protected chooseClass(key: string): void {
    // A book belongs to one class, so one that is not in the new class is dropped, with its chapters.
    if (!this.cascade.fits(this.scope().bookId, key)) {
      this.scope.update((current) => ({ ...current, bookId: '', chapterIds: [] }));
    }
    this.cascade.choose(key);
  }

  protected chooseBook(bookId: string): void {
    this.cascade.keep();
    this.scope.update((current) => ({ ...current, bookId, chapterIds: [] }));
  }

  protected toggleChapter(chapterId: string, checked: boolean): void {
    this.scope.update((current) => ({
      ...current,
      chapterIds: checked ? [...current.chapterIds.filter((id) => id !== chapterId), chapterId] : current.chapterIds.filter((id) => id !== chapterId),
    }));
  }
}
