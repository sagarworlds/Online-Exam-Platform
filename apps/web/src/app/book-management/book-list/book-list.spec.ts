import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BookList } from './book-list';

const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.includes('/v1/books');
const isClassList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/classes');

const book = (id: string, name: string, overrides: Record<string, unknown> = {}) => ({
  id, name, classId: null, className: null, subject: 'Maths', description: null, isArchived: false, chapters: [], createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z', ...overrides,
});
const inClass = (classId: string, className: string) => ({ classId, className });
const schoolClass = (id: string, name: string, overrides: Record<string, unknown> = {}) => ({
  id, name, isArchived: false, bookCount: 0, createdAtUtc: '2026-10-02T00:00:00Z', ...overrides,
});

describe('BookList', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BookList],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function open(books: unknown[], classes: unknown[] = []) {
    const fixture = TestBed.createComponent(BookList);
    fixture.detectChanges();
    httpMock.expectOne(isList).flush(books);
    const classRequest = httpMock.expectOne(isClassList);
    // The manager lists archived classes too, so the page asks for them and picks the open ones for the book form itself.
    expect(classRequest.request.params.get('includeArchived')).toBe('true');
    classRequest.flush(classes);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  const set = (fixture: ComponentFixture<BookList>, root: HTMLElement, id: string, value: string) => {
    const input = root.querySelector(`#${id}`) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
    fixture.detectChanges();
  };
  const createBook = (root: HTMLElement) => (root.querySelector('form[aria-label="New book"]') as HTMLFormElement).dispatchEvent(new Event('submit'));
  const headings = (root: HTMLElement) => Array.from(root.querySelectorAll('.book-group__title')).map((h) => h.textContent?.replace(/\s+/g, ' ').trim());
  const bookTitles = (root: HTMLElement) => Array.from(root.querySelectorAll('.book-card .card__title a')).map((a) => a.textContent?.trim());

  it('lists the books with their subject and number of chapters', () => {
    const { root } = open([
      book('b1', 'Maths Grade 10', { chapters: [{ id: 'c1' }, { id: 'c2' }] }),
      book('b2', 'Physics', { subject: null, chapters: [{ id: 'c3' }] }),
    ]);

    expect(root.textContent).toContain('Maths Grade 10');
    expect(root.textContent).toContain('2 chapters');
    expect(root.textContent).toContain('1 chapter');
    expect(root.querySelector('a[href="/admin/books/b1"]')).not.toBeNull();
  });

  it('says so when there are no books', () => {
    const { root } = open([]);

    expect(root.textContent).toContain('No books yet');
  });

  it('asks for archived books only when the box is ticked', () => {
    const { fixture, root } = open([book('b1', 'Open one')]);

    (root.querySelector('input[type="checkbox"]') as HTMLInputElement).click();
    const request = httpMock.expectOne(isList);
    expect(request.request.params.get('includeArchived')).toBe('true');
    request.flush([book('b1', 'Open one'), book('b2', 'Old one', { isArchived: true })]);
    fixture.detectChanges();

    expect(root.textContent).toContain('Old one');
    expect(root.textContent).toContain('Archived');
  });

  it('creates a book, sending blank optional fields as null, and opens it to add chapters', () => {
    const { fixture, root } = open([]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    set(fixture, root, 'book-name', 'Maths Grade 10');
    set(fixture, root, 'book-subject', '   ');
    createBook(root);

    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/books'));
    expect(post.request.body).toEqual({ name: 'Maths Grade 10', classId: null, subject: null, description: null });
    post.flush(book('b9', 'Maths Grade 10'), { status: 201, statusText: 'Created' });
    expect(navigate).toHaveBeenCalledWith(['/admin/books', 'b9']);
  });

  it('keeps Create disabled until the book has a name', () => {
    const { fixture, root } = open([]);
    const create = () => root.querySelector('form[aria-label="New book"] button.primary') as HTMLButtonElement;

    expect(create().disabled).toBe(true);
    set(fixture, root, 'book-name', 'X');

    expect(create().disabled).toBe(false);
  });

  it('shows the API error when creating is refused', () => {
    const { fixture, root } = open([]);
    set(fixture, root, 'book-name', 'X');

    createBook(root);
    httpMock.expectOne((r) => r.method === 'POST').flush({ title: 'invalid_book', detail: 'A book needs a name.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(root.textContent).toContain('A book needs a name.');
  });

  describe('classes', () => {
    const FOURTH = schoolClass('k4', '4th', { bookCount: 2 });
    const FIFTH = schoolClass('k5', '5th');
    const TENTH = schoolClass('k10', '10th');

    it('shows the class manager with the classes the API returned, archived ones too', () => {
      const { root } = open([], [TENTH, schoolClass('k3', '3rd', { isArchived: true }), FOURTH]);

      const names = Array.from(root.querySelectorAll('app-class-manager .class-list__item strong')).map((s) => s.textContent);
      expect(names).toEqual(['3rd', '4th', '10th']);
    });

    it('lists the books under a heading per class in natural order, the books without a class last', () => {
      const { root } = open([
        book('b1', 'Maths'),
        book('b2', 'English', inClass('k10', '10th')),
        book('b3', 'Science', inClass('k4', '4th')),
        book('b4', 'English', inClass('k4', '4th')),
        book('b5', 'Hindi'),
      ], [TENTH, FOURTH]);

      expect(headings(root)).toEqual(['4th', '10th', 'No class']);
      expect(bookTitles(root)).toEqual(['Science', 'English', 'English', 'Maths', 'Hindi']);
      const section = (heading: string) => {
        const title = Array.from(root.querySelectorAll('.book-group__title')).find((h) => h.textContent?.includes(heading)) as HTMLElement;
        const titles: string[] = [];
        for (let sibling = title.nextElementSibling; sibling && !sibling.classList.contains('book-group__title'); sibling = sibling.nextElementSibling) {
          titles.push(sibling.querySelector('.card__title a')?.textContent?.trim() ?? '');
        }
        return titles;
      };
      expect(section('4th')).toEqual(['Science', 'English']);
      expect(section('10th')).toEqual(['English']);
      expect(section('No class')).toEqual(['Maths', 'Hindi']);
    });

    it('marks the heading of an archived class, whose books are still listed', () => {
      const { root } = open([book('b1', 'Old English', inClass('k5', '5th'))], [schoolClass('k5', '5th', { isArchived: true })]);

      expect(headings(root)).toEqual(['5th Archived']);
      expect(root.textContent).toContain('Old English');
    });

    it('lists the books as a plain list, without headings, while no book has a class', () => {
      const { root } = open([book('b1', 'Maths'), book('b2', 'Physics')], [FOURTH]);

      expect(headings(root)).toEqual([]);
      expect(bookTitles(root)).toEqual(['Maths', 'Physics']);
    });

    it('keeps the books of a class together when none is without a class', () => {
      const { root } = open([book('b1', 'English', inClass('k4', '4th'))], [FOURTH]);

      expect(headings(root)).toEqual(['4th']);
    });

    it('offers "No class" first and then the open classes in natural order in the new book form, never an archived one', () => {
      const { root } = open([], [TENTH, schoolClass('k3', '3rd', { isArchived: true }), FIFTH, FOURTH]);

      const select = root.querySelector('#book-class') as HTMLSelectElement;
      expect(Array.from(select.options).map((o) => o.textContent?.trim())).toEqual(['No class', '4th', '5th', '10th']);
      expect(select.value).toBe('');
      expect(root.querySelector('label[for="book-class"]')?.textContent).toBe('Class (optional)');
    });

    it('creates a book under the class chosen', () => {
      const { fixture, root } = open([], [FOURTH, FIFTH]);
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      set(fixture, root, 'book-class', 'k5');
      set(fixture, root, 'book-name', 'English');
      createBook(root);

      const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/books'));
      expect(post.request.body).toEqual({ name: 'English', classId: 'k5', subject: null, description: null });
      post.flush(book('b9', 'English', inClass('k5', '5th')), { status: 201, statusText: 'Created' });
      expect(navigate).toHaveBeenCalledWith(['/admin/books', 'b9']);
    });

    it('shows why a book is refused for its class, and stays on the form', () => {
      const { fixture, root } = open([], [FOURTH]);
      set(fixture, root, 'book-class', 'k4');
      set(fixture, root, 'book-name', 'English');

      createBook(root);
      httpMock
        .expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/books'))
        .flush({ title: 'class_archived', detail: 'The class "4th" is archived and takes no new books.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(root.textContent).toContain('The class "4th" is archived and takes no new books.');
      expect((root.querySelector('#book-name') as HTMLInputElement).value).toBe('English');
    });

    it('reads the classes and the books again when the manager reports a change, so the new class is on offer and the groups are current', () => {
      const { fixture, root } = open([book('b1', 'English', inClass('k4', '4th'))], [FOURTH]);

      set(fixture, root, 'class-name', '5th');
      (root.querySelector('form[aria-label="New class"]') as HTMLFormElement).dispatchEvent(new Event('submit'));
      httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/classes')).flush(FIFTH, { status: 201, statusText: 'Created' });
      httpMock.expectOne(isClassList).flush([FOURTH, FIFTH]);
      httpMock.expectOne(isList).flush([book('b1', 'English', inClass('k4', '4th')), book('b2', 'English', inClass('k5', '5th'))]);
      fixture.detectChanges();

      const select = root.querySelector('#book-class') as HTMLSelectElement;
      expect(Array.from(select.options).map((o) => o.textContent?.trim())).toEqual(['No class', '4th', '5th']);
      expect(headings(root)).toEqual(['4th', '5th']);
    });

    it('follows a rename on the books it groups, since they carry the class name', () => {
      const { fixture, root } = open([book('b1', 'English', inClass('k4', '4th'))], [FOURTH]);

      const row = root.querySelector('.class-list__item') as HTMLElement;
      (Array.from(row.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Rename') as HTMLButtonElement).click();
      fixture.detectChanges();
      const input = root.querySelector('form[aria-label="Rename class"] input') as HTMLInputElement;
      input.value = 'Fourth';
      input.dispatchEvent(new Event('input'));
      (root.querySelector('form[aria-label="Rename class"]') as HTMLFormElement).dispatchEvent(new Event('submit'));
      httpMock.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/v1/classes/k4')).flush(schoolClass('k4', 'Fourth'));
      httpMock.expectOne(isClassList).flush([schoolClass('k4', 'Fourth')]);
      httpMock.expectOne(isList).flush([book('b1', 'English', inClass('k4', 'Fourth'))]);
      fixture.detectChanges();

      expect(headings(root)).toEqual(['Fourth']);
    });

    it('stops offering a class for the new book once it has been archived', () => {
      const { fixture, root } = open([], [FOURTH, FIFTH]);
      set(fixture, root, 'book-class', 'k5');

      const row = Array.from(root.querySelectorAll('.class-list__item')).find((li) => li.textContent?.includes('5th')) as HTMLElement;
      (Array.from(row.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Archive') as HTMLButtonElement).click();
      httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/classes/k5/archive')).flush(schoolClass('k5', '5th', { isArchived: true }));
      httpMock.expectOne(isClassList).flush([FOURTH, schoolClass('k5', '5th', { isArchived: true })]);
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();

      const select = root.querySelector('#book-class') as HTMLSelectElement;
      expect(Array.from(select.options).map((o) => o.textContent?.trim())).toEqual(['No class', '4th']);
      expect(select.value).toBe('');
    });

    it('shows the reason when the classes cannot be loaded, and still lists the books', () => {
      const fixture = TestBed.createComponent(BookList);
      fixture.detectChanges();
      httpMock.expectOne(isList).flush([book('b1', 'Maths')]);
      httpMock.expectOne(isClassList).flush({ title: 'forbidden', detail: 'You may not read classes.' }, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      expect(root.textContent).toContain('You may not read classes.');
      expect(root.textContent).toContain('Maths');
    });
  });
});
