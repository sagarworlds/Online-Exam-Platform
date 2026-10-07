import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { ExamBuilder } from './exam-builder';

describe('ExamBuilder', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExamBuilder],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  const isBooks = (r: { method: string; url: string }) => r.method === 'GET' && r.url.includes('/v1/books');

  /** Creates the page and answers its read of the books (none, unless given). */
  function open(books: unknown[] = []) {
    const fixture = TestBed.createComponent(ExamBuilder);
    fixture.detectChanges();
    httpMock.expectOne(isBooks).flush(books);
    fixture.detectChanges();
    return fixture;
  }

  function submitWith(seriesId: string) {
    const fixture = open();
    fixture.componentInstance.form.patchValue({ name: 'Maths Final', seriesId });
    fixture.componentInstance.onSubmit();
    return httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams`);
  }

  it('sends null, not an empty string, when the series field is blank', () => {
    const req = submitWith('');

    expect(req.request.body.seriesId).toBeNull();
  });

  it('sends null when the series field holds only whitespace', () => {
    const req = submitWith('   ');

    expect(req.request.body.seriesId).toBeNull();
  });

  it('sends the trimmed series id when one is given', () => {
    const req = submitWith(' 7c9e6679-7425-40de-944b-e07fc1f90ae7 ');

    expect(req.request.body.seriesId).toBe('7c9e6679-7425-40de-944b-e07fc1f90ae7');
  });

  describe('scope', () => {
    const MATHS = {
      id: 'b1', name: 'Maths Grade 10', classId: null, className: null, subject: null, description: null, isArchived: false, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
      chapters: [
        { id: 'c1', bookId: 'b1', title: 'Algebra', order: 1, isArchived: false, questionCount: 3 },
        { id: 'c2', bookId: 'b1', title: 'Geometry', order: 2, isArchived: false, questionCount: 0 },
      ],
    };
    const choose = (fixture: ReturnType<typeof open>, label: string) => {
      const radio = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input[type="radio"]'))
        .find((r) => r.parentElement?.textContent?.includes(label)) as HTMLInputElement;
      radio.click();
      fixture.detectChanges();
    };
    const pickBook = (fixture: ReturnType<typeof open>, id: string) => {
      const select = (fixture.nativeElement as HTMLElement).querySelector('select') as HTMLSelectElement;
      select.value = id;
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    const create = (fixture: ReturnType<typeof open>) => {
      fixture.componentInstance.form.patchValue({ name: 'Scoped' });
      fixture.detectChanges();
      return (fixture.nativeElement as HTMLElement).querySelector('button[type="submit"]') as HTMLButtonElement;
    };

    it('leaves the scope out of the request when the exam is not limited, so it is requested as it always was', () => {
      const req = submitWith('');

      expect('scope' in req.request.body).toBe(false);
    });

    it('sends a whole-book scope, and keeps Create off until a book is chosen', () => {
      const fixture = open([MATHS]);
      choose(fixture, 'One whole book');
      expect(create(fixture).disabled).toBe(true);

      pickBook(fixture, 'b1');
      expect(create(fixture).disabled).toBe(false);
      fixture.componentInstance.onSubmit();

      expect(httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams`).request.body.scope).toEqual({ type: 'Book', bookId: 'b1' });
    });

    it('sends chosen chapters, and needs at least one', () => {
      const fixture = open([MATHS]);
      choose(fixture, 'Chosen chapters of a book');
      pickBook(fixture, 'b1');
      expect(create(fixture).disabled).toBe(true);

      const boxes = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input[type="checkbox"]');
      expect(Array.from(boxes).map((b) => b.parentElement?.textContent?.trim())).toEqual(['1. Algebra', '2. Geometry']);
      boxes[1].click();
      fixture.detectChanges();
      expect(create(fixture).disabled).toBe(false);
      fixture.componentInstance.onSubmit();

      expect(httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams`).request.body.scope).toEqual({ type: 'Chapters', bookId: 'b1', chapterIds: ['c2'] });
    });

    it('drops the chosen chapters when another book is chosen, so a chapter of one book is never sent with another', () => {
      const other = { ...MATHS, id: 'b2', name: 'Physics', chapters: [{ id: 'c9', bookId: 'b2', title: 'Optics', order: 1, isArchived: false, questionCount: 0 }] };
      const fixture = open([MATHS, other]);
      choose(fixture, 'Chosen chapters of a book');
      pickBook(fixture, 'b1');
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input[type="checkbox"]')[0].click();
      fixture.detectChanges();

      pickBook(fixture, 'b2');

      expect(create(fixture).disabled).toBe(true);
    });

    it('does not offer archived books or chapters', () => {
      const archivedChapter = { id: 'c3', bookId: 'b1', title: 'Old chapter', order: 3, isArchived: true, questionCount: 5 };
      const fixture = open([{ ...MATHS, chapters: [...MATHS.chapters, archivedChapter] }, { ...MATHS, id: 'b9', name: 'Old book', isArchived: true }]);
      choose(fixture, 'Chosen chapters of a book');
      pickBook(fixture, 'b1');

      const root = fixture.nativeElement as HTMLElement;
      expect(root.textContent).not.toContain('Old book');
      expect(root.textContent).not.toContain('Old chapter');
    });

    it('says when there are no books to choose from', () => {
      const fixture = open([]);

      choose(fixture, 'One whole book');

      expect((fixture.nativeElement as HTMLElement).textContent).toContain('no open books yet');
    });

    it('still creates an exam, limited to nothing, for a user who may not read the question bank', () => {
      const fixture = TestBed.createComponent(ExamBuilder);
      fixture.detectChanges();
      httpMock.expectOne(isBooks).flush({ title: 'forbidden' }, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();
      vi.spyOn(console, 'error').mockImplementation(() => undefined);

      expect((fixture.nativeElement as HTMLElement).textContent).toContain('needs access to the question bank');
      expect((fixture.nativeElement as HTMLElement).querySelector('input[type="radio"]')).toBeNull();
      fixture.componentInstance.form.patchValue({ name: 'Plain' });
      fixture.componentInstance.onSubmit();
      expect('scope' in httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams`).request.body).toBe(false);
    });
  });

  it('does not send the creator, which the API takes from the access token', () => {
    const req = submitWith('');

    expect('createdBy' in req.request.body).toBe(false);
  });

  it('refuses a series id that is not a GUID, says so, and sends nothing', () => {
    const fixture = open();
    fixture.componentInstance.form.patchValue({ name: 'Maths Final', seriesId: 'series-1' });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const submit = root.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
    expect(root.textContent).toContain('A series ID looks like');

    fixture.componentInstance.onSubmit();
    httpMock.expectNone(`${environment.apiBaseUrl}/v1/exams`);
  });

  it('shows the reason the API gives when creating the exam fails', () => {
    const fixture = open();
    fixture.componentInstance.form.patchValue({ name: 'Maths Final' });
    fixture.componentInstance.onSubmit();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/v1/exams`)
      .flush({ title: 'invalid_request', detail: 'The request could not be read.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('The request could not be read.');
  });
});
