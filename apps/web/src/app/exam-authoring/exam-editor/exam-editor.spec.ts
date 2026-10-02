import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { ExamEditor } from './exam-editor';

const isExam = (r: { method: string; url: string }) => r.method === 'GET' && /\/v1\/exams\/exam-1$/.test(r.url);
const isBank = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions');

const question = (id: string, text: string, filedUnder: { chapterId: string; bookId: string } | null = null) => ({
  id,
  text,
  chapterId: filedUnder?.chapterId ?? null,
  chapterTitle: null,
  bookId: filedUnder?.bookId ?? null,
  bookName: null,
  options: [
    { id: `${id}-a`, text: 'A', isCorrect: true },
    { id: `${id}-b`, text: 'B', isCorrect: false },
  ],
  createdBy: 'u1',
  createdAtUtc: '2026-10-02T00:00:00Z',
});

function examBody(overrides: Record<string, unknown> = {}) {
  return {
    id: 'exam-1',
    seriesId: null,
    name: 'Maths Final',
    description: null,
    status: 'Draft',
    config: { totalTimeSeconds: null, markingScheme: { correctMarks: 1, incorrectMarks: 0, unattemptedMarks: 0 } },
    scheduledStartTime: '0001-01-01T00:00:00Z',
    scheduledEndTime: '0001-01-01T00:00:00Z',
    lateEntryDeadline: null,
    timeZone: 'Asia/Kolkata',
    createdBy: 'u1',
    createdAt: '2026-10-02T00:00:00Z',
    updatedAt: '2026-10-02T00:00:00Z',
    isScheduled: false,
    sections: [{ id: 's1', name: 'Algebra', timeSeconds: null, order: 1, questions: [] }],
    scope: { type: 'Independent', bookId: null, bookName: null, chapters: [] },
    ...overrides,
  };
}

describe('ExamEditor', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExamEditor],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'exam-1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function open(exam = examBody(), bank = [question('q1', 'What is 2 + 2?'), question('q2', 'Capital of France?')]) {
    const fixture = TestBed.createComponent(ExamEditor);
    fixture.detectChanges();
    httpMock.expectOne(isExam).flush(exam);
    httpMock.expectOne(isBank).flush(bank);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  const button = (root: HTMLElement, label: string) =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;

  it('shows the exam, its sections, and that it is not scheduled', () => {
    const { root } = open();

    expect(root.textContent).toContain('Maths Final');
    expect(root.textContent).toContain('Algebra');
    expect(root.textContent).toContain('Not scheduled yet.');
    expect(root.textContent).toContain('Set schedule');
  });

  it('keeps Publish disabled and says what is missing until it is scheduled and has a question', () => {
    const { root } = open();

    expect(button(root, 'Publish exam').disabled).toBe(true);
    expect(root.textContent).toContain('set a schedule');
    expect(root.textContent).toContain('add at least one question');
  });

  it('enables Publish once scheduled with a question, and publishes it', () => {
    const scheduled = examBody({
      isScheduled: true,
      scheduledStartTime: '2026-10-05T04:30:00Z',
      scheduledEndTime: '2026-10-05T07:30:00Z',
      sections: [{ id: 's1', name: 'Algebra', timeSeconds: null, order: 1, questions: [{ id: 'eq1', questionId: 'q1', order: 1, text: 'What is 2 + 2?' }] }],
    });
    const { fixture, root } = open(scheduled);
    expect(button(root, 'Publish exam').disabled).toBe(false);

    button(root, 'Publish exam').click();
    httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/exams/exam-1/publish')).flush(scheduled);
    httpMock.expectOne(isExam).flush({ ...scheduled, status: 'Published' });
    fixture.detectChanges();

    expect(root.textContent).toContain('published, so it can no longer be edited');
    expect(button(root, 'Publish exam')).toBeUndefined();
  });

  it('adds a section by name and reloads the exam', () => {
    const { fixture, root } = open();
    const input = root.querySelector('#section-name') as HTMLInputElement;
    input.value = 'Geometry';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (root.querySelector('form[aria-label="New section"]') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/exams/exam-1/sections'));
    expect(post.request.body).toEqual({ name: 'Geometry', timeSeconds: null });
    post.flush({ id: 's2', name: 'Geometry', timeSeconds: null, order: 2, questions: [] }, { status: 201, statusText: 'Created' });
    httpMock.expectOne(isExam).flush(examBody());
  });

  it('adds the chosen bank question to a section', () => {
    const { fixture, root } = open();
    const select = root.querySelector('select') as HTMLSelectElement;
    select.value = 'q2';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    button(root, 'Add question').click();

    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/exams/exam-1/sections/s1/questions'));
    expect(post.request.body).toEqual({ questionId: 'q2' });
    post.flush({ id: 'eq', questionId: 'q2', order: 1, text: 'Capital of France?' }, { status: 201, statusText: 'Created' });
    httpMock.expectOne(isExam).flush(examBody());
  });

  it('says so, and sends nothing, when Add question is pressed with no question chosen', () => {
    const { fixture, root } = open();

    button(root, 'Add question').click();
    fixture.detectChanges();

    httpMock.expectNone((r) => r.method === 'POST');
    expect(root.textContent).toContain('Choose a question to add.');
  });

  it('does not offer a question that is already in the exam', () => {
    const withQuestion = examBody({
      sections: [{ id: 's1', name: 'Algebra', timeSeconds: null, order: 1, questions: [{ id: 'eq1', questionId: 'q1', order: 1, text: 'What is 2 + 2?' }] }],
    });
    const { root } = open(withQuestion);

    const options = Array.from(root.querySelectorAll('select option')).map((o) => o.textContent?.trim());
    expect(options).toContain('Capital of France?');
    expect(options).not.toContain('What is 2 + 2?');
  });

  it('shows the API error and keeps the page when a change is refused', () => {
    const { fixture, root } = open();
    const input = root.querySelector('#section-name') as HTMLInputElement;
    input.value = 'X';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (root.querySelector('form[aria-label="New section"]') as HTMLFormElement).dispatchEvent(new Event('submit'));

    httpMock
      .expectOne((r) => r.method === 'POST')
      .flush({ title: 'exam_not_draft', detail: 'Only a draft exam can be changed; this one is already published.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(root.textContent).toContain('Only a draft exam can be changed');
    expect(root.textContent).toContain('Maths Final');
  });

  describe('scope', () => {
    const chaptersScope = { type: 'Chapters', bookId: 'b1', bookName: 'Maths Grade 10', chapters: [{ id: 'c1', title: 'Algebra' }] };
    const MATHS = {
      id: 'b1', name: 'Maths Grade 10', subject: null, description: null, isArchived: false, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
      chapters: [
        { id: 'c1', bookId: 'b1', title: 'Algebra', order: 1, isArchived: false, questionCount: 1 },
        { id: 'c2', bookId: 'b1', title: 'Geometry', order: 2, isArchived: false, questionCount: 1 },
      ],
    };
    const inAlgebra = question('q1', 'Solve x', { chapterId: 'c1', bookId: 'b1' });
    const inGeometry = question('q2', 'Angles of a triangle', { chapterId: 'c2', bookId: 'b1' });
    const elsewhere = question('q3', 'Optics question', { chapterId: 'c9', bookId: 'b2' });
    const unfiled = question('q4', 'Unfiled question');

    /** Opens a scoped exam; its bank is read with the API's own book filter, so the page expects that request. */
    function openScoped(exam = examBody({ scope: chaptersScope }), bank = [inAlgebra, inGeometry]) {
      const fixture = TestBed.createComponent(ExamEditor);
      fixture.detectChanges();
      httpMock.expectOne(isExam).flush(exam);
      const request = httpMock.expectOne(isBank);
      request.flush(bank);
      fixture.detectChanges();
      return { fixture, root: fixture.nativeElement as HTMLElement, request: request.request };
    }

    it('says what an unlimited exam draws from', () => {
      const { root } = open();

      expect(root.textContent).toContain('Questions come from');
      expect(root.textContent).toContain('Any question in the bank');
    });

    it('says what a chapter-wise exam draws from, and reads the bank with the book filter', () => {
      const { root, request } = openScoped();

      expect(root.textContent).toContain('Maths Grade 10: Algebra');
      expect(request.params.get('bookId')).toBe('b1');
    });

    it('offers only the questions inside the exam scope', () => {
      const { root } = openScoped(undefined, [inAlgebra, inGeometry, elsewhere, unfiled]);

      const options = Array.from(root.querySelectorAll('select option')).map((o) => o.textContent?.trim());
      expect(options).toContain('Solve x');
      expect(options).not.toContain('Angles of a triangle');
      expect(options).not.toContain('Optics question');
      expect(options).not.toContain('Unfiled question');
      expect(root.textContent).toContain("Only questions from this exam's chapters are offered.");
    });

    it('offers every question of the book to a whole-book exam', () => {
      const { root } = openScoped(examBody({ scope: { type: 'Book', bookId: 'b1', bookName: 'Maths Grade 10', chapters: [] } }), [inAlgebra, inGeometry, elsewhere]);

      const options = Array.from(root.querySelectorAll('select option')).map((o) => o.textContent?.trim());
      expect(options).toContain('Solve x');
      expect(options).toContain('Angles of a triangle');
      expect(options).not.toContain('Optics question');
    });

    it('says what to do when the scope has no questions left to add', () => {
      const { root } = openScoped(undefined, []);

      expect(root.textContent).toContain('There are none left to add');
    });

    it('changes the scope: loads the books, saves, shows the new scope and reads the bank for it', () => {
      const { fixture, root } = open();
      button(root, 'Change').click();
      fixture.detectChanges();
      httpMock.expectOne((r) => r.method === 'GET' && r.url.includes('/v1/books')).flush([MATHS]);
      fixture.detectChanges();

      const radio = Array.from(root.querySelectorAll<HTMLInputElement>('input[type="radio"]')).find((r) => r.parentElement?.textContent?.includes('One whole book')) as HTMLInputElement;
      radio.click();
      fixture.detectChanges();
      const select = root.querySelector('app-exam-scope-fields select') as HTMLSelectElement;
      select.value = 'b1';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      button(root, 'Save scope').click();

      const put = httpMock.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/v1/exams/exam-1/scope'));
      expect(put.request.body).toEqual({ type: 'Book', bookId: 'b1' });
      put.flush(examBody({ scope: { type: 'Book', bookId: 'b1', bookName: 'Maths Grade 10', chapters: [] } }));
      httpMock.expectOne(isExam).flush(examBody({ scope: { type: 'Book', bookId: 'b1', bookName: 'Maths Grade 10', chapters: [] } }));
      expect(httpMock.expectOne(isBank).request.params.get('bookId')).toBe('b1');
      fixture.detectChanges();

      expect(root.textContent).toContain('The whole book Maths Grade 10');
      expect(root.querySelector('app-exam-scope-fields')).toBeNull();
    });

    it('shows why a scope was refused, and stays on the form with the choice intact', () => {
      const { fixture, root } = openScoped();
      button(root, 'Change').click();
      fixture.detectChanges();
      httpMock.expectOne((r) => r.method === 'GET' && r.url.includes('/v1/books')).flush([MATHS]);
      fixture.detectChanges();
      button(root, 'Save scope').click();

      httpMock
        .expectOne((r) => r.method === 'PUT' && r.url.endsWith('/scope'))
        .flush({ title: 'question_outside_scope', detail: '1 question(s) already in this exam are outside that scope.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(root.textContent).toContain('already in this exam are outside that scope');
      expect(root.querySelector('app-exam-scope-fields')).not.toBeNull();
    });

    it('can back out of changing the scope without sending anything', () => {
      const { fixture, root } = open();
      button(root, 'Change').click();
      fixture.detectChanges();
      httpMock.expectOne((r) => r.method === 'GET' && r.url.includes('/v1/books')).flush([MATHS]);
      fixture.detectChanges();

      button(root, 'Cancel').click();
      fixture.detectChanges();

      expect(root.querySelector('app-exam-scope-fields')).toBeNull();
    });

    it('says so when the user may not read the question bank, instead of offering a choice it cannot fill', () => {
      const { fixture, root } = open();
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      button(root, 'Change').click();
      fixture.detectChanges();

      httpMock.expectOne((r) => r.method === 'GET' && r.url.includes('/v1/books')).flush({}, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();

      expect(root.textContent).toContain('needs access to the question bank');
      expect(button(root, 'Save scope').disabled).toBe(true);
    });

    it('does not offer to change the scope of a published exam', () => {
      const { root } = open(examBody({ status: 'Published', isScheduled: true, scheduledStartTime: '2026-10-05T04:30:00Z', scheduledEndTime: '2026-10-05T07:30:00Z', scope: chaptersScope }));

      expect(root.textContent).toContain('Maths Grade 10: Algebra');
      expect(button(root, 'Change')).toBeUndefined();
    });
  });

  it('shows a published exam read-only', () => {
    const { root } = open(examBody({ status: 'Published', isScheduled: true, scheduledStartTime: '2026-10-05T04:30:00Z', scheduledEndTime: '2026-10-05T07:30:00Z' }));

    expect(root.querySelector('form[aria-label="New section"]')).toBeNull();
    expect(button(root, 'Add question')).toBeUndefined();
    expect(root.textContent).toContain('no longer be edited');
  });
});
