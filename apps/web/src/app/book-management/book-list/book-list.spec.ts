import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BookList } from './book-list';

const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.includes('/v1/books');

const book = (id: string, name: string, overrides: Record<string, unknown> = {}) => ({
  id, name, subject: 'Maths', description: null, isArchived: false, chapters: [], createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z', ...overrides,
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

  function open(books: unknown[]) {
    const fixture = TestBed.createComponent(BookList);
    fixture.detectChanges();
    httpMock.expectOne(isList).flush(books);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

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
    const set = (id: string, value: string) => {
      const input = root.querySelector(`#${id}`) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };

    set('book-name', 'Maths Grade 10');
    set('book-subject', '   ');
    fixture.detectChanges();
    (root.querySelector('form[aria-label="New book"]') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/books'));
    expect(post.request.body).toEqual({ name: 'Maths Grade 10', subject: null, description: null });
    post.flush(book('b9', 'Maths Grade 10'), { status: 201, statusText: 'Created' });
    expect(navigate).toHaveBeenCalledWith(['/admin/books', 'b9']);
  });

  it('keeps Create disabled until the book has a name', () => {
    const { fixture, root } = open([]);

    expect((root.querySelector('button.primary') as HTMLButtonElement).disabled).toBe(true);
    const input = root.querySelector('#book-name') as HTMLInputElement;
    input.value = 'X';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect((root.querySelector('button.primary') as HTMLButtonElement).disabled).toBe(false);
  });

  it('shows the API error when creating is refused', () => {
    const { fixture, root } = open([]);
    const input = root.querySelector('#book-name') as HTMLInputElement;
    input.value = 'X';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (root.querySelector('form[aria-label="New book"]') as HTMLFormElement).dispatchEvent(new Event('submit'));
    httpMock.expectOne((r) => r.method === 'POST').flush({ title: 'invalid_book', detail: 'A book needs a name.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(root.textContent).toContain('A book needs a name.');
  });
});
