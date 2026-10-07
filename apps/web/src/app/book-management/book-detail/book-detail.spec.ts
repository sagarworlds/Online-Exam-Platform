import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BookDetail } from './book-detail';

const url = (r: { url: string }, suffix: string) => r.url.endsWith(`/v1/books/b1${suffix}`);
const isClassList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/classes');

const schoolClass = (id: string, name: string, isArchived = false) => ({ id, name, isArchived, bookCount: 0, createdAtUtc: '2026-10-02T00:00:00Z' });
const FOURTH = schoolClass('k4', '4th');
const FIFTH = schoolClass('k5', '5th');
const TENTH = schoolClass('k10', '10th');
const ARCHIVED = schoolClass('k3', '3rd', true);

const chapter = (id: string, order: number, title: string, overrides: Record<string, unknown> = {}) => ({
  id, bookId: 'b1', title, order, isArchived: false, questionCount: 0, ...overrides,
});

const book = (overrides: Record<string, unknown> = {}) => ({
  id: 'b1', name: 'Maths Grade 10', classId: null, className: null, subject: 'Maths', description: 'Algebra and geometry', isArchived: false,
  chapters: [chapter('c1', 1, 'Algebra', { questionCount: 12 }), chapter('c2', 2, 'Geometry')],
  createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z', ...overrides,
});

describe('BookDetail', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BookDetail],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'b1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function open(initial = book(), classes: unknown[] = []) {
    const fixture = TestBed.createComponent(BookDetail);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.method === 'GET' && url(r, '')).flush(initial);
    const classRequest = httpMock.expectOne(isClassList);
    // Archived classes are asked for too, so the book's own class can be told apart as archived.
    expect(classRequest.request.params.get('includeArchived')).toBe('true');
    classRequest.flush(classes);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  const button = (root: HTMLElement, label: string, within?: Element) =>
    Array.from((within ?? root).querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const rowOf = (root: HTMLElement, title: string) =>
    Array.from(root.querySelectorAll('tbody tr')).find((tr) => tr.textContent?.includes(title)) as HTMLElement;
  const type = (fixture: ComponentFixture<BookDetail>, input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  it('shows the book, its chapters in order, and how many questions each holds', () => {
    const { root } = open();

    expect(root.querySelector('h1')?.textContent).toContain('Maths Grade 10');
    const cells = (tr: Element) => Array.from(tr.querySelectorAll('td')).slice(0, 3).map((td) => td.textContent?.trim());
    const rows = Array.from(root.querySelectorAll('tbody tr')).map(cells);
    expect(rows).toEqual([['1', 'Algebra', '12'], ['2', 'Geometry', '0']]);
  });

  it('adds a chapter and shows the book the API answers with', () => {
    const { fixture, root } = open();
    type(fixture, root.querySelector('#chapter-title') as HTMLInputElement, 'Trigonometry');

    (root.querySelector('form[aria-label="New chapter"]') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const post = httpMock.expectOne((r) => r.method === 'POST' && url(r, '/chapters'));
    expect(post.request.body).toEqual({ title: 'Trigonometry' });
    post.flush(book({ chapters: [...book().chapters, chapter('c3', 3, 'Trigonometry')] }), { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(rowOf(root, 'Trigonometry')).toBeDefined();
    expect((root.querySelector('#chapter-title') as HTMLInputElement).value).toBe('');
  });

  it('renames a chapter in place', () => {
    const { fixture, root } = open();

    button(root, 'Rename', rowOf(root, 'Algebra')).click();
    fixture.detectChanges();
    const input = root.querySelector('form[aria-label="Rename chapter"] input') as HTMLInputElement;
    expect(input.value).toBe('Algebra');
    type(fixture, input, 'Algebra I');
    (root.querySelector('form[aria-label="Rename chapter"]') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const put = httpMock.expectOne((r) => r.method === 'PUT' && url(r, '/chapters/c1'));
    expect(put.request.body).toEqual({ title: 'Algebra I' });
    put.flush(book({ chapters: [chapter('c1', 1, 'Algebra I'), chapter('c2', 2, 'Geometry')] }));
    fixture.detectChanges();

    expect(root.querySelector('form[aria-label="Rename chapter"]')).toBeNull();
    expect(rowOf(root, 'Algebra I')).toBeDefined();
  });

  it('can back out of a rename without sending anything', () => {
    const { fixture, root } = open();

    button(root, 'Rename', rowOf(root, 'Algebra')).click();
    fixture.detectChanges();
    button(root, 'Cancel').click();
    fixture.detectChanges();

    expect(root.querySelector('form[aria-label="Rename chapter"]')).toBeNull();
  });

  it('archives and restores a chapter, and says archived chapters keep their questions', () => {
    const { fixture, root } = open();

    button(root, 'Archive', rowOf(root, 'Algebra')).click();
    const archive = httpMock.expectOne((r) => r.method === 'POST' && url(r, '/chapters/c1/archive'));
    archive.flush(book({ chapters: [chapter('c1', 1, 'Algebra', { isArchived: true, questionCount: 12 }), chapter('c2', 2, 'Geometry')] }));
    fixture.detectChanges();

    expect(rowOf(root, 'Algebra').textContent).toContain('Archived');
    expect(rowOf(root, 'Algebra').textContent).toContain('12');
    button(root, 'Restore', rowOf(root, 'Algebra')).click();
    httpMock.expectOne((r) => r.method === 'POST' && url(r, '/chapters/c1/restore')).flush(book());
    fixture.detectChanges();

    expect(rowOf(root, 'Algebra').textContent).toContain('Open');
  });

  it('saves the details, and keeps Save off until something changed', () => {
    const { fixture, root } = open();
    const save = () => button(root, 'Save details');
    expect(save().disabled).toBe(true);

    type(fixture, root.querySelector('#detail-name') as HTMLInputElement, 'Maths Grade 11');
    type(fixture, root.querySelector('#detail-description') as HTMLInputElement, '  ');
    expect(save().disabled).toBe(false);
    (root.querySelector('form[aria-label="Book details"]') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const put = httpMock.expectOne((r) => r.method === 'PUT' && url(r, ''));
    expect(put.request.body).toEqual({ name: 'Maths Grade 11', classId: null, subject: 'Maths', description: null });
    put.flush(book({ name: 'Maths Grade 11', description: null }));
    fixture.detectChanges();

    expect(root.textContent).toContain('Saved.');
    expect(save().disabled).toBe(true);
  });

  it('archives a book, then offers to restore it and no longer takes chapters', () => {
    const { fixture, root } = open();

    button(root, 'Archive book').click();
    httpMock.expectOne((r) => r.method === 'POST' && url(r, '/archive')).flush(book({ isArchived: true }));
    fixture.detectChanges();

    expect(root.textContent).toContain('This book is archived');
    expect(button(root, 'Restore book')).toBeDefined();
    expect(root.querySelector('form[aria-label="New chapter"]')).toBeNull();
  });

  it('shows the reason, and leaves the page as it was, when a change is refused', () => {
    const { fixture, root } = open();
    type(fixture, root.querySelector('#chapter-title') as HTMLInputElement, 'algebra');

    (root.querySelector('form[aria-label="New chapter"]') as HTMLFormElement).dispatchEvent(new Event('submit'));
    httpMock
      .expectOne((r) => r.method === 'POST' && url(r, '/chapters'))
      .flush({ title: 'duplicate_chapter', detail: 'This book already has a chapter called "algebra".' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(root.textContent).toContain('already has a chapter');
    expect(root.querySelectorAll('tbody tr').length).toBe(2);
  });

  describe('class', () => {
    const classSelect = (root: HTMLElement) => root.querySelector('#detail-class') as HTMLSelectElement;
    const optionTexts = (root: HTMLElement) => Array.from(classSelect(root).options).map((o) => o.textContent?.trim());
    const choose = (fixture: ComponentFixture<BookDetail>, root: HTMLElement, value: string) => {
      classSelect(root).value = value;
      classSelect(root).dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    const submit = (root: HTMLElement) => (root.querySelector('form[aria-label="Book details"]') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const inFourth = () => book({ classId: 'k4', className: '4th' });

    it('shows the class under the heading when the book has one, and nothing when it has none', () => {
      const { root } = open(inFourth(), [FOURTH]);
      expect(root.querySelector('.book-class')?.textContent).toContain('Class: 4th');
      expect(root.querySelector('h1')?.textContent).toContain('Maths Grade 10');
    });

    it('shows no class line for a book without a class', () => {
      const { root } = open(book(), [FOURTH]);

      expect(root.querySelector('.book-class')).toBeNull();
    });

    it("offers no class and the open classes in natural order, with the book's class selected", () => {
      const { root } = open(book({ classId: 'k5', className: '5th' }), [TENTH, FIFTH, ARCHIVED, FOURTH]);

      expect(optionTexts(root)).toEqual(['No class', '4th', '5th', '10th']);
      expect(classSelect(root).value).toBe('k5');
      expect(root.querySelector('label[for="detail-class"]')?.textContent).toBe('Class (optional)');
    });

    it('selects "No class" for a book without one', () => {
      const { root } = open(book(), [FOURTH]);

      expect(classSelect(root).value).toBe('');
    });

    it("still shows the book's class, selected, when that class is archived, and offers no other archived class", () => {
      const { root } = open(book({ classId: 'k3', className: '3rd' }), [FOURTH, ARCHIVED, schoolClass('k2', '2nd', true)]);

      expect(optionTexts(root)).toEqual(['No class', '3rd (archived)', '4th']);
      expect(classSelect(root).value).toBe('k3');
    });

    it("keeps the book's class on offer when the classes could not be read, and says why", () => {
      const fixture = TestBed.createComponent(BookDetail);
      fixture.detectChanges();
      httpMock.expectOne((r) => r.method === 'GET' && url(r, '')).flush(inFourth());
      httpMock.expectOne(isClassList).flush({ title: 'forbidden', detail: 'You may not read classes.' }, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      expect(root.textContent).toContain('You may not read classes.');
      expect(optionTexts(root)).toEqual(['No class', '4th']);
      expect(classSelect(root).value).toBe('k4');
    });

    it('saves the class chosen, with the rest of the details', () => {
      const { fixture, root } = open(inFourth(), [FOURTH, FIFTH]);
      expect(button(root, 'Save details').disabled).toBe(true);

      choose(fixture, root, 'k5');
      expect(button(root, 'Save details').disabled).toBe(false);
      submit(root);

      const put = httpMock.expectOne((r) => r.method === 'PUT' && url(r, ''));
      expect(put.request.body).toEqual({ name: 'Maths Grade 10', classId: 'k5', subject: 'Maths', description: 'Algebra and geometry' });
      put.flush(book({ classId: 'k5', className: '5th' }));
      fixture.detectChanges();

      expect(root.textContent).toContain('Saved.');
      expect(root.querySelector('.book-class')?.textContent).toContain('Class: 5th');
      expect(classSelect(root).value).toBe('k5');
    });

    it('takes a book out of its class when "No class" is chosen, sending null because the update replaces everything', () => {
      const { fixture, root } = open(inFourth(), [FOURTH]);

      choose(fixture, root, '');
      submit(root);

      const put = httpMock.expectOne((r) => r.method === 'PUT' && url(r, ''));
      expect(put.request.body.classId).toBeNull();
      put.flush(book());
      fixture.detectChanges();

      expect(root.querySelector('.book-class')).toBeNull();
    });

    it('keeps the class when only something else is changed', () => {
      const { fixture, root } = open(inFourth(), [FOURTH]);
      type(fixture, root.querySelector('#detail-name') as HTMLInputElement, 'Maths Grade 11');

      submit(root);

      const put = httpMock.expectOne((r) => r.method === 'PUT' && url(r, ''));
      expect(put.request.body).toEqual({ name: 'Maths Grade 11', classId: 'k4', subject: 'Maths', description: 'Algebra and geometry' });
      put.flush(inFourth());
    });

    it('keeps an archived class when only something else is changed', () => {
      const { fixture, root } = open(book({ classId: 'k3', className: '3rd' }), [ARCHIVED]);
      type(fixture, root.querySelector('#detail-name') as HTMLInputElement, 'Maths Grade 11');

      submit(root);

      const put = httpMock.expectOne((r) => r.method === 'PUT' && url(r, ''));
      expect(put.request.body.classId).toBe('k3');
      put.flush(book({ classId: 'k3', className: '3rd' }));
    });

    it('shows why a class was refused, and leaves the form as it was', () => {
      const { fixture, root } = open(book(), [FOURTH]);
      choose(fixture, root, 'k4');

      submit(root);
      httpMock
        .expectOne((r) => r.method === 'PUT' && url(r, ''))
        .flush({ title: 'class_archived', detail: 'The class "4th" is archived and takes no new books.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(root.textContent).toContain('The class "4th" is archived and takes no new books.');
      expect(classSelect(root).value).toBe('k4');
      expect(root.textContent).not.toContain('Saved.');
    });
  });

  it('says so when the book cannot be loaded', () => {
    const fixture = TestBed.createComponent(BookDetail);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && url(r, '')).flush({ title: 'book_not_found', detail: 'No book matches the given id.' }, { status: 404, statusText: 'Not Found' });
    httpMock.expectOne(isClassList).flush([]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No book matches the given id.');
  });
});
