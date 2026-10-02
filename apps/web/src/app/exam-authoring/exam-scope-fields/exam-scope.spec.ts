import { ExamScopeDto } from '../exam.models';
import { NO_SCOPE, describeScope, isScopeComplete, selectionOf, toScopeRequest } from './exam-scope';

describe('exam scope helpers', () => {
  it('is complete when nothing limits the exam, when a book is chosen for Book, and when a book has a chapter for Chapters', () => {
    expect(isScopeComplete(NO_SCOPE)).toBe(true);
    expect(isScopeComplete({ type: 'Book', bookId: '', chapterIds: [] })).toBe(false);
    expect(isScopeComplete({ type: 'Book', bookId: 'b1', chapterIds: [] })).toBe(true);
    expect(isScopeComplete({ type: 'Chapters', bookId: 'b1', chapterIds: [] })).toBe(false);
    expect(isScopeComplete({ type: 'Chapters', bookId: '', chapterIds: ['c1'] })).toBe(false);
    expect(isScopeComplete({ type: 'Chapters', bookId: 'b1', chapterIds: ['c1'] })).toBe(true);
  });

  it('sends only what each type needs', () => {
    expect(toScopeRequest({ type: 'Independent', bookId: 'stale', chapterIds: ['stale'] })).toEqual({ type: 'Independent' });
    expect(toScopeRequest({ type: 'Book', bookId: 'b1', chapterIds: ['stale'] })).toEqual({ type: 'Book', bookId: 'b1' });
    expect(toScopeRequest({ type: 'Chapters', bookId: 'b1', chapterIds: ['c1', 'c2'] })).toEqual({ type: 'Chapters', bookId: 'b1', chapterIds: ['c1', 'c2'] });
    expect(toScopeRequest('Independent')).toEqual({ type: 'Independent' });
  });

  it('starts editing from the scope an exam has', () => {
    const scope: ExamScopeDto = { type: 'Chapters', bookId: 'b1', bookName: 'Maths', chapters: [{ id: 'c1', title: 'Algebra' }, { id: 'c2', title: null }] };

    expect(selectionOf(scope)).toEqual({ type: 'Chapters', bookId: 'b1', chapterIds: ['c1', 'c2'] });
    expect(selectionOf({ type: 'Book', bookId: 'b1', bookName: 'Maths', chapters: [] })).toEqual({ type: 'Book', bookId: 'b1', chapterIds: [] });
    expect(selectionOf(undefined)).toEqual(NO_SCOPE);
  });

  it('never hands out the shared empty selection to be changed', () => {
    const first = selectionOf(undefined);
    first.chapterIds.push('x');

    expect(selectionOf(undefined).chapterIds).toEqual([]);
    expect(NO_SCOPE.chapterIds).toEqual([]);
  });

  it('describes what an exam draws from in a line', () => {
    expect(describeScope(undefined)).toBe('Any question in the bank');
    expect(describeScope({ type: 'Independent', bookId: null, bookName: null, chapters: [] })).toBe('Any question in the bank');
    expect(describeScope({ type: 'Book', bookId: 'b1', bookName: 'Maths', chapters: [] })).toBe('The whole book Maths');
    expect(describeScope({ type: 'Chapters', bookId: 'b1', bookName: 'Maths', chapters: [{ id: 'c1', title: 'Algebra' }, { id: 'c2', title: 'Geometry' }] })).toBe('Maths: Algebra, Geometry');
  });

  it('says so, rather than showing a blank, when the bank no longer has the book or a chapter', () => {
    expect(describeScope({ type: 'Book', bookId: 'b1', bookName: null, chapters: [] })).toContain('no longer in the bank');
    expect(describeScope({ type: 'Chapters', bookId: 'b1', bookName: 'Maths', chapters: [{ id: 'c1', title: null }] })).toContain('no longer in the bank');
  });
});
