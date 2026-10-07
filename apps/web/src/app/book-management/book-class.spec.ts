import { signal } from '@angular/core';
import { ANY_CLASS, ClassCascade, NO_CLASS, bookOptionLabel, booksInClass, classChoicesOf, classKeyOf, compareNames } from './book-class';
import { BookDto } from './book.models';

const book = (id: string, name: string, schoolClass: { id: string; name: string } | null = null): BookDto => ({
  id, name, classId: schoolClass?.id ?? null, className: schoolClass?.name ?? null, subject: null, description: null, isArchived: false, chapters: [],
  createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
});
const FOURTH = { id: 'k4', name: '4th' };
const TENTH = { id: 'k10', name: '10th' };
const SECOND = { id: 'k2', name: '2nd' };

describe('book class helpers', () => {
  it('orders names naturally, so 2nd comes before 10th', () => {
    expect(['10th', '2nd', '4th'].sort(compareNames)).toEqual(['2nd', '4th', '10th']);
  });

  it('keys a book by its class, or by "no class"', () => {
    expect(classKeyOf(book('b1', 'English', FOURTH))).toBe('k4');
    expect(classKeyOf(book('b2', 'Maths'))).toBe(NO_CLASS);
  });

  it('lists each class of the books once, in natural order, and leaves out books without one', () => {
    const books = [book('b1', 'English', TENTH), book('b2', 'Maths'), book('b3', 'Science', SECOND), book('b4', 'English', TENTH)];

    expect(classChoicesOf(books)).toEqual([{ id: 'k2', name: '2nd' }, { id: 'k10', name: '10th' }]);
    expect(classChoicesOf([book('b2', 'Maths')])).toEqual([]);
  });

  it('keeps the books of one class, or lists every book with the classed ones first and the others last, each group in its order', () => {
    const books = [book('b1', 'Maths'), book('b2', 'English', TENTH), book('b3', 'Hindi'), book('b4', 'Science', SECOND)];

    expect(booksInClass(books, 'k10').map((b) => b.id)).toEqual(['b2']);
    expect(booksInClass(books, NO_CLASS).map((b) => b.id)).toEqual(['b1', 'b3']);
    expect(booksInClass(books, ANY_CLASS).map((b) => b.id)).toEqual(['b4', 'b2', 'b1', 'b3']);
    expect(books.map((b) => b.id)).toEqual(['b1', 'b2', 'b3', 'b4']);
  });

  it('treats a book from an API that predates classes, with no class fields at all, as having none', () => {
    const old = { ...book('b1', 'Maths'), classId: undefined, className: undefined } as unknown as BookDto;

    expect(classKeyOf(old)).toBe(NO_CLASS);
    expect(classChoicesOf([old])).toEqual([]);
    expect(booksInClass([old], NO_CLASS)).toEqual([old]);
    expect(new ClassCascade(signal([old]), signal('')).offered()).toBe(false);
  });

  it('labels a book with its class only while every class is listed', () => {
    const english = book('b1', 'English', FOURTH);

    expect(bookOptionLabel(english, ANY_CLASS)).toBe('4th · English');
    expect(bookOptionLabel(english, 'k4')).toBe('English');
    expect(bookOptionLabel(book('b2', 'Maths'), ANY_CLASS)).toBe('Maths');
  });
});

describe('ClassCascade', () => {
  const books = signal<readonly BookDto[]>([book('m', 'Maths'), book('e4', 'English', FOURTH), book('e10', 'English', TENTH)]);
  const bound = signal('');
  let cascade: ClassCascade;

  beforeEach(() => {
    books.set([book('m', 'Maths'), book('e4', 'English', FOURTH), book('e10', 'English', TENTH)]);
    bound.set('');
    cascade = new ClassCascade(books, bound);
  });

  it('starts on any class, offering the classes and "no class"', () => {
    expect(cascade.key()).toBe(ANY_CLASS);
    expect(cascade.offered()).toBe(true);
    expect(cascade.offersNoClass()).toBe(true);
    expect(cascade.classes().map((c) => c.name)).toEqual(['4th', '10th']);
  });

  it('is not offered while no book has a class', () => {
    books.set([book('m', 'Maths')]);

    expect(cascade.offered()).toBe(false);
    expect(cascade.key()).toBe(ANY_CLASS);
  });

  it('follows the bound book until a class is chosen', () => {
    bound.set('e10');
    expect(cascade.key()).toBe('k10');

    cascade.choose('k4');
    bound.set('e4');
    expect(cascade.key()).toBe('k4');
  });

  it('falls back to any class when the class chosen is no longer on offer', () => {
    cascade.choose('k4');
    books.set([book('m', 'Maths'), book('e10', 'English', TENTH)]);

    expect(cascade.key()).toBe(ANY_CLASS);
  });

  it('knows which books fit a class', () => {
    expect(cascade.fits('e4', 'k4')).toBe(true);
    expect(cascade.fits('e4', 'k10')).toBe(false);
    expect(cascade.fits('e4', ANY_CLASS)).toBe(true);
    expect(cascade.fits('m', NO_CLASS)).toBe(true);
    expect(cascade.fits('', 'k4')).toBe(true);
    expect(cascade.fits('unknown', 'k4')).toBe(false);
  });

  it('keeps the class as shown once a book is chosen under it', () => {
    cascade.keep();
    bound.set('e4');

    expect(cascade.key()).toBe(ANY_CLASS);
  });
});
