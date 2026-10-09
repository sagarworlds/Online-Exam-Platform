import { Translate, englishTranslate } from '../../i18n/i18n.service';
import { ExamScopeDto, ExamScopeRequest, ExamScopeType } from '../exam.models';

/** What the author has chosen in the scope fields, before it is sent: ids are strings, '' means "none chosen". */
export interface ScopeSelection {
  type: ExamScopeType;
  bookId: string;
  chapterIds: string[];
}

/** No limit: the questions may come from anywhere in the bank. */
export const NO_SCOPE: ScopeSelection = { type: 'Independent', bookId: '', chapterIds: [] };

/** Whether the choice is complete enough to send: a book for Book, and a book with at least one chapter for Chapters. */
export function isScopeComplete(selection: ScopeSelection): boolean {
  switch (selection.type) {
    case 'Independent':
      return true;
    case 'Book':
      return selection.bookId !== '';
    case 'Chapters':
      return selection.bookId !== '' && selection.chapterIds.length > 0;
  }
}

/** The request body for a complete choice. Only what the type needs is sent, so the API never sees a stray book id. */
export function toScopeRequest(selection: ExamScopeType | ScopeSelection): ExamScopeRequest {
  if (typeof selection === 'string') {
    return { type: selection };
  }

  switch (selection.type) {
    case 'Independent':
      return { type: 'Independent' };
    case 'Book':
      return { type: 'Book', bookId: selection.bookId };
    case 'Chapters':
      return { type: 'Chapters', bookId: selection.bookId, chapterIds: selection.chapterIds };
  }
}

/** The choice that reproduces an exam's current scope, to start editing it from. */
export function selectionOf(scope: ExamScopeDto | null | undefined): ScopeSelection {
  if (!scope || scope.type === 'Independent') {
    return { ...NO_SCOPE, chapterIds: [] };
  }

  return { type: scope.type, bookId: scope.bookId ?? '', chapterIds: scope.chapters.map((chapter) => chapter.id) };
}

/**
 * One line saying what an exam's questions come from, for lists and the editor. The words are English unless a translator is given, so
 * a page that has not been translated yet reads exactly as before.
 */
export function describeScope(scope: ExamScopeDto | null | undefined, t: Translate = englishTranslate): string {
  if (!scope || scope.type === 'Independent') {
    return t('exams.scope.any');
  }

  // The class is shown beside the book, since the same book name can exist under several classes. The brackets read the same in
  // every language, so they are not a message of their own.
  const bookName = scope.bookName ?? t('exams.scope.noBook');
  const book = scope.className ? `${bookName} (${scope.className})` : bookName;
  if (scope.type === 'Book') {
    return t('exams.scope.whole', { book });
  }

  const titles = scope.chapters.map((chapter) => chapter.title ?? t('exams.scope.noChapter')).join(', ');
  return t('exams.scope.chapters', { book, titles });
}
