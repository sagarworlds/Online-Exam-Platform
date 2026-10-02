import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BookDto } from '../../book-management/book.models';
import { BookChapterPicker, isCompletePlacement, NO_PLACEMENT } from './book-chapter-picker';

const chapter = (id: string, order: number, title: string, isArchived = false) => ({ id, bookId: 'b1', title, order, isArchived, questionCount: 0 });
const book = (id: string, name: string, chapters: ReturnType<typeof chapter>[], isArchived = false): BookDto => ({
  id, name, subject: null, description: null, isArchived, chapters, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
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
});

describe('isCompletePlacement', () => {
  it('accepts nothing chosen, or a chapter; refuses a book without a chapter', () => {
    expect(isCompletePlacement({ bookId: '', chapterId: '' })).toBe(true);
    expect(isCompletePlacement({ bookId: 'b1', chapterId: 'c1' })).toBe(true);
    expect(isCompletePlacement({ bookId: 'b1', chapterId: '' })).toBe(false);
  });
});
