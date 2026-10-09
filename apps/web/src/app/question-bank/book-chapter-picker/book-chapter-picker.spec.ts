import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BookDto } from '../../book-management/book.models';
import { BookChapterPicker, isCompletePlacement, NO_PLACEMENT, Placement } from './book-chapter-picker';

interface SchoolClass {
  id: string;
  name: string;
}

const chapter = (id: string, order: number, title: string, isArchived = false) => ({ id, bookId: 'b1', title, order, isArchived, questionCount: 0 });
const book = (id: string, name: string, chapters: ReturnType<typeof chapter>[], isArchived = false, schoolClass: SchoolClass | null = null): BookDto => ({
  id, name, subject: null, description: null, classId: schoolClass?.id ?? null, className: schoolClass?.name ?? null, isArchived, chapters, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
});
const MATHS = book('b1', 'Maths', [chapter('c1', 1, 'Algebra'), chapter('c2', 2, 'Geometry'), chapter('c3', 3, 'Old chapter', true)]);
const OLD_BOOK = book('b2', 'Old Physics', [], true);
const EMPTY = book('b3', 'Empty', []);

describe('BookChapterPicker', () => {
  let fixture: ComponentFixture<BookChapterPicker>;
  let root: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [BookChapterPicker], providers: [provideRouter([])] }).compileComponents();
    fixture = TestBed.createComponent(BookChapterPicker);
    fixture.componentRef.setInput('books', [MATHS, OLD_BOOK, EMPTY]);
    fixture.componentRef.setInput('idPrefix', 'pick');
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  });

  const choose = (id: string, value: string) => {
    const select = root.querySelector(`#${id}`) as HTMLSelectElement;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };
  const optionTexts = (id: string) => Array.from(root.querySelectorAll(`#${id} option`)).map((o) => o.textContent?.trim());

  it('offers open books only, and no chapter until a book is chosen', () => {
    expect(optionTexts('pick-book')).toEqual(['Not filed under a book', 'Maths', 'Empty']);
    expect(root.querySelector('#pick-chapter')).toBeNull();
  });

  it('offers the open chapters of the chosen book, in order', () => {
    choose('pick-book', 'b1');

    expect(optionTexts('pick-chapter')).toEqual(['Choose a chapter…', '1. Algebra', '2. Geometry']);
    expect(fixture.componentInstance.value()).toEqual({ bookId: 'b1', chapterId: '' });
  });

  it('reports the chosen chapter', () => {
    choose('pick-book', 'b1');
    choose('pick-chapter', 'c2');

    expect(fixture.componentInstance.value()).toEqual({ bookId: 'b1', chapterId: 'c2' });
  });

  it('drops the chapter when the book changes, so a chapter never belongs to the wrong book', () => {
    choose('pick-book', 'b1');
    choose('pick-chapter', 'c2');
    choose('pick-book', 'b3');

    expect(fixture.componentInstance.value()).toEqual({ bookId: 'b3', chapterId: '' });
  });

  it('clears everything when "no book" is chosen again', () => {
    choose('pick-book', 'b1');
    choose('pick-chapter', 'c1');
    choose('pick-book', '');

    expect(fixture.componentInstance.value()).toEqual(NO_PLACEMENT);
    expect(root.querySelector('#pick-chapter')).toBeNull();
  });

  it('says where to add a chapter when the book has none open', () => {
    choose('pick-book', 'b3');

    expect(root.textContent).toContain('This book has no open chapters');
    expect(root.querySelector('a[href="/admin/books/b3"]')).not.toBeNull();
  });

  it('shows a choice made from outside, such as after filing keeps the same chapter selected', () => {
    fixture.componentRef.setInput('value', { bookId: 'b1', chapterId: 'c2' });
    fixture.detectChanges();

    expect((root.querySelector('#pick-book') as HTMLSelectElement).value).toBe('b1');
    expect((root.querySelector('#pick-chapter') as HTMLSelectElement).value).toBe('c2');
  });

  it('uses its own prefix, so two pickers can share a page, and the labels it is given', () => {
    fixture.componentRef.setInput('idPrefix', 'file');
    fixture.componentRef.setInput('bookLabel', 'File under book');
    fixture.componentRef.setInput('noBookLabel', 'Choose a book…');
    fixture.detectChanges();

    expect(root.querySelector('#file-book')).not.toBeNull();
    expect(root.querySelector('#pick-book')).toBeNull();
    expect(root.querySelector('label[for="file-book"]')?.textContent).toBe('File under book');
    expect(optionTexts('file-book')[0]).toBe('Choose a book…');
  });

  it('shows no class select while no book has a class, so the picker is what it was before classes', () => {
    expect(root.querySelector('#pick-class')).toBeNull();
    expect(optionTexts('pick-book')).toEqual(['Not filed under a book', 'Maths', 'Empty']);
  });
});

describe('BookChapterPicker with classes', () => {
  const FOURTH: SchoolClass = { id: 'k4', name: '4th' };
  const FIFTH: SchoolClass = { id: 'k5', name: '5th' };
  const english = (id: string, schoolClass: SchoolClass, chapters: string[]) =>
    book(id, 'English', chapters.map((title, i) => ({ ...chapter(`${id}-c${i + 1}`, i + 1, title), bookId: id })), false, schoolClass);
  // English exists in two classes, Maths has none, and the only book of the 3rd class is archived.
  const ENGLISH_4 = english('e4', FOURTH, ['Nouns', 'Verbs']);
  const ENGLISH_5 = english('e5', FIFTH, ['Poems']);
  const GRAMMAR_3 = book('g3', 'Grammar', [], true, { id: 'k3', name: '3rd' });
  const BOOKS = [MATHS, ENGLISH_5, ENGLISH_4, GRAMMAR_3];

  let fixture: ComponentFixture<BookChapterPicker>;
  let root: HTMLElement;

  function open(books: readonly BookDto[] = BOOKS, value?: Placement) {
    fixture = TestBed.createComponent(BookChapterPicker);
    fixture.componentRef.setInput('books', books);
    fixture.componentRef.setInput('idPrefix', 'pick');
    if (value) fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [BookChapterPicker], providers: [provideRouter([])] }).compileComponents();
  });

  const select = (id: string) => root.querySelector(`#${id}`) as HTMLSelectElement;
  const choose = (id: string, value: string) => {
    select(id).value = value;
    select(id).dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };
  const optionTexts = (id: string) => Array.from(select(id).options).map((o) => o.textContent?.trim());
  const optionValues = (id: string) => Array.from(select(id).options).map((o) => o.value);

  it('puts a Class select in front of the Book select', () => {
    open();

    const ids = Array.from(root.querySelectorAll('select')).map((s) => s.id);
    expect(ids).toEqual(['pick-class', 'pick-book']);
    expect(root.querySelector('label[for="pick-class"]')?.textContent).toBe('Class');
  });

  it('offers any class, each class that has an open book in natural order, and no class', () => {
    open([...BOOKS, book('t10', 'Science', [], false, { id: 'k10', name: '10th' }), book('t2', 'Science', [], false, { id: 'k2', name: '2nd' })]);

    // The 3rd class has only an archived book, so it is not offered.
    expect(optionTexts('pick-class')).toEqual(['Any class', '2nd', '4th', '5th', '10th', 'No class']);
    expect((select('pick-class')).value).toBe('');
  });

  it('does not offer "No class" when every open book has a class', () => {
    open([ENGLISH_4, ENGLISH_5]);

    expect(optionTexts('pick-class')).toEqual(['Any class', '4th', '5th']);
  });

  it('labels each book with its class while any class is chosen, the books of a class first', () => {
    open();

    expect(optionTexts('pick-book')).toEqual(['Not filed under a book', '4th · English', '5th · English', 'Maths']);
  });

  it('narrows the books to the class chosen, now with plain names', () => {
    open();

    choose('pick-class', 'k4');
    expect(optionTexts('pick-book')).toEqual(['Not filed under a book', 'English']);
    expect(optionValues('pick-book')).toEqual(['', 'e4']);

    choose('pick-class', 'k5');
    expect(optionValues('pick-book')).toEqual(['', 'e5']);
  });

  it('lists the books without a class under "No class"', () => {
    open();

    choose('pick-class', 'none');

    expect(optionTexts('pick-book')).toEqual(['Not filed under a book', 'Maths']);
  });

  it('works through class, book and chapter, and reports just the book and the chapter', () => {
    open();

    choose('pick-class', 'k4');
    choose('pick-book', 'e4');
    expect(optionTexts('pick-chapter')).toEqual(['Choose a chapter…', '1. Nouns', '2. Verbs']);
    choose('pick-chapter', 'e4-c2');

    expect(fixture.componentInstance.value()).toEqual({ bookId: 'e4', chapterId: 'e4-c2' });
    expect(Object.keys(fixture.componentInstance.value()).sort()).toEqual(['bookId', 'chapterId']);
  });

  it('clears the book and the chapter when the class changes to one that does not hold the book', () => {
    open();
    choose('pick-class', 'k4');
    choose('pick-book', 'e4');
    choose('pick-chapter', 'e4-c1');

    choose('pick-class', 'k5');

    expect(fixture.componentInstance.value()).toEqual(NO_PLACEMENT);
    expect(select('pick-book').value).toBe('');
    expect(root.querySelector('#pick-chapter')).toBeNull();
    expect(select('pick-class').value).toBe('k5');
  });

  it('keeps the book and the chapter when the class changes to the one that holds the book, or to any class', () => {
    open();
    choose('pick-class', 'k4');
    choose('pick-book', 'e4');
    choose('pick-chapter', 'e4-c1');

    choose('pick-class', 'k4');
    expect(fixture.componentInstance.value()).toEqual({ bookId: 'e4', chapterId: 'e4-c1' });

    choose('pick-class', '');
    expect(fixture.componentInstance.value()).toEqual({ bookId: 'e4', chapterId: 'e4-c1' });
    expect(select('pick-book').value).toBe('e4');
  });

  it('clears the book of a class when "No class" is chosen, and keeps a book that has none', () => {
    open();
    choose('pick-class', 'k4');
    choose('pick-book', 'e4');

    choose('pick-class', 'none');
    expect(fixture.componentInstance.value()).toEqual(NO_PLACEMENT);

    choose('pick-book', 'b1');
    choose('pick-chapter', 'c2');
    choose('pick-class', 'none');
    expect(fixture.componentInstance.value()).toEqual({ bookId: 'b1', chapterId: 'c2' });
  });

  it('does not turn "any class" into the class of the book chosen under it', () => {
    open();

    choose('pick-book', 'e5');

    expect(select('pick-class').value).toBe('');
    expect(optionTexts('pick-book')).toContain('4th · English');
  });

  describe('a placement set from outside, as when a filed question is edited', () => {
    it('starts the class select on the class of the book the placement names', () => {
      open(BOOKS, { bookId: 'e5', chapterId: 'e5-c1' });

      expect(select('pick-class').value).toBe('k5');
      expect(select('pick-book').value).toBe('e5');
      expect(select('pick-chapter').value).toBe('e5-c1');
      expect(optionTexts('pick-book')).toEqual(['Not filed under a book', 'English']);
    });

    it('starts on "No class" for a book without a class', () => {
      open(BOOKS, { bookId: 'b1', chapterId: 'c1' });

      expect(select('pick-class').value).toBe('none');
      expect(select('pick-book').value).toBe('b1');
    });

    it('follows a placement that is set after the picker was shown', () => {
      open();

      fixture.componentRef.setInput('value', { bookId: 'e4', chapterId: '' });
      fixture.detectChanges();

      expect(select('pick-class').value).toBe('k4');
      expect(select('pick-book').value).toBe('e4');
    });

    it('lets the book the placement names win over a class that does not hold it', () => {
      open();
      choose('pick-class', 'k4');

      fixture.componentRef.setInput('value', { bookId: 'e5', chapterId: 'e5-c1' });
      fixture.detectChanges();

      expect(select('pick-class').value).toBe('k5');
      expect(select('pick-book').value).toBe('e5');
    });

    it('goes back to the empty choice when the placement is cleared from outside', () => {
      open(BOOKS, { bookId: 'e5', chapterId: 'e5-c1' });

      fixture.componentRef.setInput('value', NO_PLACEMENT);
      fixture.detectChanges();

      expect(select('pick-book').value).toBe('');
      expect(root.querySelector('#pick-chapter')).toBeNull();
    });

    it('does not move the class for a placement on a book that is archived, which is not offered', () => {
      open(BOOKS, { bookId: 'g3', chapterId: '' });

      expect(select('pick-class').value).toBe('');
    });
  });

  it('keeps the ids and labels of the book and chapter selects, with its own prefix for the class', () => {
    open();
    fixture.componentRef.setInput('idPrefix', 'file-q1');
    fixture.componentRef.setInput('bookLabel', 'File under book');
    fixture.detectChanges();

    expect(root.querySelector('#file-q1-class')).not.toBeNull();
    expect(root.querySelector('label[for="file-q1-book"]')?.textContent).toBe('File under book');
    expect(root.querySelector('#pick-class')).toBeNull();
  });
});

describe('isCompletePlacement', () => {
  it('accepts nothing chosen, or a chapter; refuses a book without a chapter', () => {
    expect(isCompletePlacement({ bookId: '', chapterId: '' })).toBe(true);
    expect(isCompletePlacement({ bookId: 'b1', chapterId: 'c1' })).toBe(true);
    expect(isCompletePlacement({ bookId: 'b1', chapterId: '' })).toBe(false);
  });
});
