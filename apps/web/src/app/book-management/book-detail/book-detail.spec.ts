import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BookDetail } from './book-detail';

const url = (r: { url: string }, suffix: string) => r.url.endsWith(`/v1/books/b1${suffix}`);

const chapter = (id: string, order: number, title: string, overrides: Record<string, unknown> = {}) => ({
  id, bookId: 'b1', title, order, isArchived: false, questionCount: 0, ...overrides,
});

const book = (overrides: Record<string, unknown> = {}) => ({
  id: 'b1', name: 'Maths Grade 10', subject: 'Maths', description: 'Algebra and geometry', isArchived: false,
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

  function open(initial = book()) {
    const fixture = TestBed.createComponent(BookDetail);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.method === 'GET' && url(r, '')).flush(initial);
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
    expect(put.request.body).toEqual({ name: 'Maths Grade 11', subject: 'Maths', description: null });
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

  it('says so when the book cannot be loaded', () => {
    const fixture = TestBed.createComponent(BookDetail);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && url(r, '')).flush({ title: 'book_not_found', detail: 'No book matches the given id.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No book matches the given id.');
  });
});
