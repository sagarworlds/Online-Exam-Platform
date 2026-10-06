import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { ExamEditor } from './exam-editor';

const isExam = (r: { method: string; url: string }) => r.method === 'GET' && /\/v1\/exams\/exam-1$/.test(r.url);
const isTopics = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions/topics');
const isBank = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions');

const question = (id: string, text: string, filedUnder: { chapterId: string; bookId: string } | null = null) => ({
  id,
  text,
  chapterId: filedUnder?.chapterId ?? null,
  chapterTitle: null,
  bookId: filedUnder?.bookId ?? null,
  bookName: null,
  options: [
    { id: `${id}-a`, text: 'A', isCorrect: true, isPinned: false },
    { id: `${id}-b`, text: 'B', isCorrect: false, isPinned: false },
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
    config: {
      totalTimeSeconds: null,
      maxAttempts: 1,
      resultReleaseMode: 'Instant',
      resultReleaseTime: null,
      markingScheme: { correctMarks: 1, incorrectMarks: 0, unattemptedMarks: 0 },
    },
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

  afterEach(() => {
    // The topics for the draw picker load with the page; tests about something else need not answer them.
    httpMock.match(isTopics).forEach((request) => request.flush([]));
    httpMock.verify();
  });

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

    expect(root.textContent).toContain('published, so its questions and schedule can no longer be edited');
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

  it('draws random questions into a section and reads the exam again', () => {
    const { fixture, root } = open();
    button(root, 'Add random questions…').click();
    fixture.detectChanges();
    const difficulty = Array.from(root.querySelectorAll('label')).find((l) => l.textContent?.trim().startsWith('Difficulty'))?.querySelector('select') as HTMLSelectElement;
    difficulty.value = 'easy';
    difficulty.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    button(root, 'Draw').click();

    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/exams/exam-1/sections/s1/questions/draw'));
    expect(post.request.body).toEqual({ count: 5, difficulty: 'easy', topic: null });
    post.flush([]);
    httpMock.expectOne(isExam).flush(examBody());
  });

  it('shows the API’s reason when a draw finds too few questions', () => {
    const { fixture, root } = open();
    button(root, 'Add random questions…').click();
    fixture.detectChanges();

    button(root, 'Draw').click();
    httpMock
      .expectOne((r) => r.method === 'POST' && r.url.endsWith('/draw'))
      .flush({ title: 'not_enough_questions', detail: '5 questions were asked for but only 2 match and are not already in this exam.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(root.textContent).toContain('only 2 match');
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

  it('links a published exam to its candidates and attempts, and a draft does not', () => {
    const published = open(examBody({ status: 'Published' })).root;
    const link = Array.from(published.querySelectorAll('a')).find((a) => a.textContent?.includes('Candidates and attempts'));
    expect(link?.getAttribute('href')).toBe('/exams/exam-1/attempts');
    TestBed.resetTestingModule();
  });

  it('offers no candidates link on a draft, which nobody has been invited to', () => {
    const { root } = open();

    expect(Array.from(root.querySelectorAll('a')).some((a) => a.textContent?.includes('Candidates and attempts'))).toBe(false);
  });

  it('shows a published exam read-only', () => {
    const { root } = open(examBody({ status: 'Published', isScheduled: true, scheduledStartTime: '2026-10-05T04:30:00Z', scheduledEndTime: '2026-10-05T07:30:00Z' }));

    expect(root.querySelector('form[aria-label="New section"]')).toBeNull();
    expect(button(root, 'Add question')).toBeUndefined();
    expect(root.textContent).toContain('no longer be edited');
  });
  describe('answer review', () => {
    const config = (overrides: Record<string, unknown>) => ({
      totalTimeSeconds: null,
      maxAttempts: 1,
      resultReleaseMode: 'Instant',
      resultReleaseTime: null,
      markingScheme: { correctMarks: 1, incorrectMarks: 0, unattemptedMarks: 0 },
      ...overrides,
    });
    const isRelease = (r: { method: string; url: string }) => r.method === 'PUT' && r.url.endsWith('/v1/exams/exam-1/result-release');
    const radio = (root: HTMLElement, label: string) =>
      Array.from(root.querySelectorAll<HTMLInputElement>('input[type="radio"]')).find((r) => r.parentElement?.textContent?.includes(label)) as HTMLInputElement;

    /** Opens the Answer review form: its button is "Edit" (the scope card's is "Change"). */
    function openForm(exam = examBody()) {
      const opened = open(exam);
      button(opened.root, 'Edit').click();
      opened.fixture.detectChanges();
      return opened;
    }

    it('says the answers are shown right after submitting, which is how an exam starts', () => {
      const { root } = open();

      expect(root.textContent).toContain('as soon as they submit');
      expect(button(root, 'Release answers now')).toBeUndefined();
    });

    it('says from when a scheduled exam shows its answers', () => {
      const { root } = open(examBody({ config: config({ resultReleaseMode: 'Scheduled', resultReleaseTime: '2026-10-08T09:00:00Z' }) }));

      expect(root.textContent).toContain('Candidates see which of their answers were right from');
      expect(root.textContent).toContain('2026');
    });

    it('holds a manual draft back, and says it can be released once the exam is published', () => {
      const { root } = open(examBody({ config: config({ resultReleaseMode: 'Manual' }) }));

      expect(root.textContent).toContain('Held back until you release them. You can do that once the exam is published.');
      expect(button(root, 'Release answers now')).toBeUndefined();
    });

    it('lets the author pick a mode and save it, then shows what the server stored', () => {
      const { fixture, root } = openForm();

      radio(root, 'When I release them').click();
      fixture.detectChanges();
      button(root, 'Save').click();

      const put = httpMock.expectOne(isRelease);
      expect(put.request.body).toEqual({ mode: 'Manual', releaseTime: null });
      const manual = examBody({ config: config({ resultReleaseMode: 'Manual' }) });
      put.flush(manual);
      httpMock.expectOne(isExam).flush(manual);
      fixture.detectChanges();

      expect(root.textContent).toContain('Held back until you release them');
      expect(root.querySelector('app-exam-release-fields')).toBeNull();
    });

    it('keeps Save off for a scheduled release until it has a time, then sends it as UTC', () => {
      const { fixture, root } = openForm();
      radio(root, 'From a set time').click();
      fixture.detectChanges();
      expect(button(root, 'Save').disabled).toBe(true);

      const time = root.querySelector('input[type="datetime-local"]') as HTMLInputElement;
      time.value = '2026-10-08T14:30';
      time.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(button(root, 'Save').disabled).toBe(false);
      button(root, 'Save').click();

      const put = httpMock.expectOne(isRelease);
      expect(put.request.body).toEqual({ mode: 'Scheduled', releaseTime: new Date('2026-10-08T14:30').toISOString() });
      put.flush(examBody());
      httpMock.expectOne(isExam).flush(examBody());
    });

    it('starts the form from what the exam is set to, and Cancel leaves it unchanged', () => {
      const { fixture, root } = openForm(examBody({ config: config({ resultReleaseMode: 'Manual' }) }));
      expect(radio(root, 'When I release them').checked).toBe(true);

      button(root, 'Cancel').click();
      fixture.detectChanges();

      expect(root.querySelector('app-exam-release-fields')).toBeNull();
      expect(root.textContent).toContain('Held back until you release them');
    });

    it('shows the API’s reason when the choice is refused, and keeps the form open', () => {
      const { fixture, root } = openForm();
      button(root, 'Save').click();

      httpMock.expectOne(isRelease).flush({ title: 'exam_archived', detail: 'This exam is archived and can no longer be changed.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(root.textContent).toContain('This exam is archived and can no longer be changed.');
      expect(root.querySelector('app-exam-release-fields')).not.toBeNull();
    });

    it('offers “Release answers now” on a published manual exam, and releases it', () => {
      const { fixture, root } = open(examBody({ status: 'Published', config: config({ resultReleaseMode: 'Manual' }) }));

      button(root, 'Release answers now').click();

      const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/exams/exam-1/results/release'));
      const released = examBody({ status: 'Published', config: config({ resultReleaseMode: 'Manual', resultReleaseTime: '2026-10-05T10:00:00Z' }) });
      post.flush(released);
      httpMock.expectOne(isExam).flush(released);
      fixture.detectChanges();

      expect(root.textContent).toContain('Released on');
      expect(button(root, 'Release answers now')).toBeUndefined();
    });

    it('offers no manual release on a published exam that is not set to it', () => {
      const { root } = open(examBody({ status: 'Published' }));

      expect(button(root, 'Release answers now')).toBeUndefined();
    });

    it('can still be edited once the exam is published, unlike its questions, scope and schedule', () => {
      const { root } = open(examBody({ status: 'Published' }));

      expect(button(root, 'Edit')).toBeDefined();
      expect(button(root, 'Change')).toBeUndefined();
      expect(root.textContent).not.toContain('Set schedule');
    });
  });
  describe('marking scheme', () => {
    const isMarks = (r: { method: string; url: string }) => r.method === 'PUT' && r.url.endsWith('/v1/exams/exam-1/marking-scheme');
    const card = (root: HTMLElement) => root.querySelector('app-exam-marking-scheme') as HTMLElement;
    const edit = (root: HTMLElement) => card(root).querySelector('button[aria-label="Edit marking scheme"]') as HTMLButtonElement;
    const withMarks = (correctMarks: number, incorrectMarks: number) =>
      examBody({
        config: {
          totalTimeSeconds: null,
          maxAttempts: 1,
          resultReleaseMode: 'Instant',
          resultReleaseTime: null,
          markingScheme: { correctMarks, incorrectMarks, unattemptedMarks: 0 },
        },
      });

    it('shows the marks of a draft and lets the author edit them', () => {
      const { root } = open(withMarks(4, -1));

      expect(card(root).textContent).toContain('Correct answer: 4');
      expect(card(root).textContent).toContain('Incorrect answer: -1');
      expect(edit(root)).not.toBeNull();
    });

    it('sends the new marks, then shows what the server stored', () => {
      const { fixture, root } = open(withMarks(1, 0));
      edit(root).click();
      fixture.detectChanges();

      const correct = root.querySelector('#marks-correct') as HTMLInputElement;
      correct.value = '4';
      correct.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      button(card(root), 'Save').click();

      const put = httpMock.expectOne(isMarks);
      expect(put.request.body).toEqual({ correctMarks: 4, incorrectMarks: 0, unattemptedMarks: 0, partialCredit: false });
      const stored = withMarks(4, 0);
      put.flush(stored);
      httpMock.expectOne(isExam).flush(stored);
      fixture.detectChanges();

      expect(card(root).textContent).toContain('Correct answer: 4');
      expect(root.querySelector('#marks-correct')).toBeNull();
    });

    it.each(['Published', 'Archived'])('cannot be changed once the exam is %s', (status) => {
      const { root } = open({ ...withMarks(1, 0), status });

      expect(edit(root)).toBeNull();
      expect(card(root).textContent).toContain('fixed once an exam is published');
    });
  });

  describe('shuffling', () => {
    const isShuffle = (r: { method: string; url: string }) => r.method === 'PUT' && r.url.endsWith('/v1/exams/exam-1/shuffle');
    const card = (root: HTMLElement) => root.querySelector('app-exam-shuffle') as HTMLElement;
    const boxes = (root: HTMLElement) => Array.from(card(root).querySelectorAll<HTMLInputElement>('input[type="checkbox"]'));
    const withShuffle = (shuffleQuestions: boolean, shuffleOptions: boolean) =>
      examBody({
        config: {
          totalTimeSeconds: null,
          shuffleQuestions,
          shuffleOptions,
          maxAttempts: 1,
          resultReleaseMode: 'Instant',
          resultReleaseTime: null,
          markingScheme: { correctMarks: 1, incorrectMarks: 0, unattemptedMarks: 0 },
        },
      });

    it('shows the two choices of a draft, and no Save until one is changed', () => {
      const { root } = open(withShuffle(true, false));

      expect(boxes(root).map((b) => b.checked)).toEqual([true, false]);
      expect(button(card(root), 'Save')).toBeUndefined();
    });

    it('sends both choices, then shows what the server stored', () => {
      const { fixture, root } = open(withShuffle(false, false));

      boxes(root)[1].click();
      fixture.detectChanges();
      button(card(root), 'Save').click();

      const put = httpMock.expectOne(isShuffle);
      expect(put.request.body).toEqual({ shuffleQuestions: false, shuffleOptions: true });
      const stored = withShuffle(false, true);
      put.flush(stored);
      httpMock.expectOne(isExam).flush(stored);
      fixture.detectChanges();

      expect(boxes(root).map((b) => b.checked)).toEqual([false, true]);
      expect(button(card(root), 'Save')).toBeUndefined();
    });

    it('drops an unsaved change on Cancel', () => {
      const { fixture, root } = open(withShuffle(false, false));

      boxes(root)[0].click();
      fixture.detectChanges();
      button(card(root), 'Cancel').click();
      fixture.detectChanges();

      expect(boxes(root).map((b) => b.checked)).toEqual([false, false]);
    });

    it.each(['Published', 'Archived'])('cannot be changed once the exam is %s', (status) => {
      const { root } = open({ ...withShuffle(true, false), status });

      expect(boxes(root)).toHaveLength(0);
      expect(card(root).textContent).toContain('Questions: shuffled');
      expect(card(root).textContent).toContain('fixed once an exam is published');
    });
  });

  describe('attempts allowed', () => {
    const isLimit = (r: { method: string; url: string }) => r.method === 'PUT' && r.url.endsWith('/v1/exams/exam-1/attempt-limit');
    const withLimit = (maxAttempts: number, release = 'Instant') =>
      examBody({
        config: {
          totalTimeSeconds: null,
          maxAttempts,
          resultReleaseMode: release,
          resultReleaseTime: null,
          markingScheme: { correctMarks: 1, incorrectMarks: 0, unattemptedMarks: 0 },
        },
      });
    const card = (root: HTMLElement) => root.querySelector('app-exam-attempt-limit') as HTMLElement;
    const field = (root: HTMLElement) => root.querySelector('#attempt-limit') as HTMLInputElement;

    function openForm(exam = withLimit(1)) {
      const opened = open(exam);
      (card(opened.root).querySelector('button[aria-label="Edit attempts allowed"]') as HTMLButtonElement).click();
      opened.fixture.detectChanges();
      return opened;
    }

    function type(fixture: { detectChanges(): void }, root: HTMLElement, value: string) {
      field(root).value = value;
      field(root).dispatchEvent(new Event('input'));
      fixture.detectChanges();
    }

    it('says how many attempts every candidate has, which is one unless the author chose more', () => {
      expect(card(open(withLimit(1)).root).textContent).toContain('Every candidate can sit this exam once.');
    });

    it('sends the new number, then shows what the server stored', () => {
      const { fixture, root } = openForm();
      expect(field(root).value).toBe('1');

      type(fixture, root, '3');
      button(card(root), 'Save').click();

      const put = httpMock.expectOne(isLimit);
      expect(put.request.body).toEqual({ maxAttempts: 3 });
      const stored = withLimit(3);
      put.flush(stored);
      httpMock.expectOne(isExam).flush(stored);
      fixture.detectChanges();

      expect(card(root).textContent).toContain('Every candidate can sit this exam 3 times.');
      expect(field(root)).toBeNull();
    });

    it('can be changed after the exam is published, because it changes nothing that is asked or scored', () => {
      const published = { ...withLimit(1), status: 'Published' };

      const { fixture, root } = openForm(published);
      type(fixture, root, '2');
      button(card(root), 'Save').click();

      expect(httpMock.expectOne(isLimit).request.body).toEqual({ maxAttempts: 2 });
    });

    it('cannot be changed once the exam is archived', () => {
      const { root } = open({ ...withLimit(1), status: 'Archived' });

      expect(card(root).querySelector('button')).toBeNull();
    });

    it('shows the reason and keeps the form open when the server refuses the number', () => {
      const { fixture, root } = openForm();
      type(fixture, root, '4');
      button(card(root), 'Save').click();

      httpMock
        .expectOne(isLimit)
        .flush({ title: 'exam_archived', detail: 'This exam is archived.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(root.textContent).toContain('This exam is archived.');
      expect(field(root).value).toBe('4');
    });

    it('warns, next to the answer review, when more than one attempt is combined with answers shown right away', () => {
      const { root } = open(withLimit(2, 'Instant'));

      expect(card(root).textContent).toContain('candidates see the correct answers before their next attempt');
    });

    it('does not warn when the answers are held back', () => {
      expect(card(open(withLimit(2, 'Manual')).root).textContent).not.toContain('correct answers before');
    });

    it('does not warn for a single attempt', () => {
      expect(card(open(withLimit(1, 'Instant')).root).textContent).not.toContain('correct answers before');
    });
  });

  describe('putting a draft right', () => {
    const withQuestions = () =>
      examBody({
        sections: [
          {
            id: 's1', name: 'Algebra', timeSeconds: 900, order: 1,
            questions: [
              { id: 'eq1', questionId: 'q1', order: 1, text: 'What is 2 + 2?' },
              { id: 'eq2', questionId: 'q2', order: 2, text: 'Capital of France?' },
            ],
          },
        ],
      });
    const isDelete = (suffix: string) => (r: { method: string; url: string }) => r.method === 'DELETE' && r.url.endsWith(`/v1/exams/exam-1${suffix}`);
    const press = (fixture: { detectChanges(): void }, root: HTMLElement, label: string) => {
      button(root, label).click();
      fixture.detectChanges();
    };

    it('takes a question out of its section and reads the exam again', () => {
      const { fixture, root } = open(withQuestions());

      (root.querySelectorAll('li button')[1] as HTMLButtonElement).click();

      httpMock.expectOne(isDelete('/sections/s1/questions/q2')).flush(null, { status: 204, statusText: 'No Content' });
      const after = withQuestions();
      after.sections[0].questions.pop();
      httpMock.expectOne(isExam).flush(after);
      fixture.detectChanges();

      expect(root.querySelectorAll('li').length).toBe(1);
      // It can be added again now: the picker offers it.
      expect(Array.from(root.querySelectorAll('select option')).map((o) => o.textContent?.trim())).toContain('Capital of France?');
    });

    it('removes a section after asking, and reads the exam again', () => {
      const { fixture, root } = open(withQuestions());

      press(fixture, root, 'Remove section');
      httpMock.expectNone((r) => r.method === 'DELETE');
      press(fixture, root, 'Remove section');

      httpMock.expectOne(isDelete('/sections/s1')).flush(null, { status: 204, statusText: 'No Content' });
      httpMock.expectOne(isExam).flush(examBody({ sections: [] }));
      fixture.detectChanges();

      expect(root.textContent).toContain('No sections yet.');
    });

    it('renames a section and sends the time limit it already has, so a rename never removes it', () => {
      const { fixture, root } = open(withQuestions());
      press(fixture, root, 'Rename');
      const field = root.querySelector('input[aria-label="Section name"]') as HTMLInputElement;
      field.value = 'Geometry';
      field.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      press(fixture, root, 'Save name');

      const put = httpMock.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/v1/exams/exam-1/sections/s1'));
      expect(put.request.body).toEqual({ name: 'Geometry', timeSeconds: 900 });
      put.flush(null, { status: 204, statusText: 'No Content' });
      const renamed = withQuestions();
      renamed.sections[0].name = 'Geometry';
      httpMock.expectOne(isExam).flush(renamed);
      fixture.detectChanges();

      expect(root.querySelector('h3')?.textContent?.trim()).toBe('1. Geometry');
      expect(root.querySelector('input[aria-label="Section name"]')).toBeNull();
    });

    it('shows why a change was refused and keeps the page', () => {
      const { fixture, root } = open(withQuestions());
      (root.querySelectorAll('li button')[0] as HTMLButtonElement).click();

      httpMock
        .expectOne(isDelete('/sections/s1/questions/q1'))
        .flush({ title: 'exam_not_draft', detail: 'Only a draft exam can be changed; this one is already published.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(root.textContent).toContain('Only a draft exam can be changed');
      expect(root.querySelectorAll('li').length).toBe(2);
    });

    it('offers none of this on a published exam', () => {
      const published = { ...withQuestions(), status: 'Published' };
      const { root } = open(published);

      expect(root.querySelector('li button')).toBeNull();
      expect(button(root, 'Rename')).toBeUndefined();
      expect(button(root, 'Remove section')).toBeUndefined();
      expect(button(root, 'Delete draft')).toBeUndefined();
    });

    describe('name and description', () => {
      it('opens a form with what the exam has, saves it, and shows the new name', () => {
        const { fixture, root } = open(examBody({ description: 'Chapters 1 to 4' }));
        press(fixture, root, 'Edit details');
        expect((root.querySelector('#exam-name') as HTMLInputElement).value).toBe('Maths Final');
        expect((root.querySelector('#exam-description') as HTMLTextAreaElement).value).toBe('Chapters 1 to 4');

        const name = root.querySelector('#exam-name') as HTMLInputElement;
        name.value = 'Maths Final 2026';
        name.dispatchEvent(new Event('input'));
        fixture.detectChanges();
        press(fixture, root, 'Save details');

        const put = httpMock.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/v1/exams/exam-1/details'));
        expect(put.request.body).toEqual({ name: 'Maths Final 2026', description: 'Chapters 1 to 4' });
        put.flush(examBody({ name: 'Maths Final 2026' }));
        httpMock.expectOne(isExam).flush(examBody({ name: 'Maths Final 2026', description: 'Chapters 1 to 4' }));
        fixture.detectChanges();

        expect(root.querySelector('h1')?.textContent).toContain('Maths Final 2026');
        expect(root.querySelector('app-exam-details-form')).toBeNull();
      });

      it('is offered on a published exam as well, because it changes nothing that is asked or scored', () => {
        const { root } = open(examBody({ status: 'Published' }));

        expect(button(root, 'Edit details')).toBeDefined();
      });

      it('is not offered for an archived exam', () => {
        const { root } = open(examBody({ status: 'Archived' }));

        expect(button(root, 'Edit details')).toBeUndefined();
      });

      it('stays open with the typing when the API refuses', () => {
        const { fixture, root } = open();
        press(fixture, root, 'Edit details');
        const name = root.querySelector('#exam-name') as HTMLInputElement;
        name.value = 'Kept';
        name.dispatchEvent(new Event('input'));
        fixture.detectChanges();
        press(fixture, root, 'Save details');

        httpMock
          .expectOne((r) => r.method === 'PUT' && r.url.endsWith('/details'))
          .flush({ title: 'invalid_exam_config', detail: 'Invalid exam config: An exam name must be at most 255 characters.' }, { status: 400, statusText: 'Bad Request' });
        fixture.detectChanges();

        expect(root.textContent).toContain('An exam name must be at most 255 characters.');
        expect((root.querySelector('#exam-name') as HTMLInputElement).value).toBe('Kept');
      });

      it('can be backed out of with nothing sent', () => {
        const { fixture, root } = open();
        press(fixture, root, 'Edit details');

        press(fixture, root, 'Cancel');

        expect(root.querySelector('app-exam-details-form')).toBeNull();
      });
    });

    describe('deleting the draft', () => {
      it('asks first, then deletes and goes back to the list without reading the deleted exam', () => {
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        const { fixture, root } = open();

        press(fixture, root, 'Delete draft');
        expect(root.querySelector('[role="alertdialog"]')?.textContent).toContain('Delete “Maths Final” for good?');
        httpMock.expectNone((r) => r.method === 'DELETE');
        press(fixture, root, 'Delete draft'); // the confirming one

        httpMock.expectOne(isDelete('')).flush(null, { status: 204, statusText: 'No Content' });
        httpMock.expectNone(isExam);

        expect(navigate).toHaveBeenCalledWith(['/exams']);
      });

      it('can be backed out of with nothing deleted', () => {
        const { fixture, root } = open();
        press(fixture, root, 'Delete draft');

        press(fixture, root, 'Cancel');

        expect(root.querySelector('[role="alertdialog"]')).toBeNull();
        httpMock.expectNone((r) => r.method === 'DELETE');
      });

      it('shows the reason and stays on the page when the API refuses, such as when candidates were invited', () => {
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        const { fixture, root } = open();
        press(fixture, root, 'Delete draft');
        press(fixture, root, 'Delete draft');

        httpMock
          .expectOne(isDelete(''))
          .flush({ title: 'exam_not_deletable', detail: 'This exam cannot be deleted. Candidates have been invited to it; revoke the invitations first.' }, { status: 409, statusText: 'Conflict' });
        fixture.detectChanges();

        expect(root.textContent).toContain('revoke the invitations first');
        expect(navigate).not.toHaveBeenCalled();
        expect(button(root, 'Delete draft').disabled).toBe(false);
      });
    });
  });

  describe('copying and printing (FR-23)', () => {
    const isProtection = (r: { method: string; url: string }) => r.method === 'PUT' && r.url.endsWith('/v1/exams/exam-1/content-protection');
    const withProtection = (contentProtection: boolean | undefined, status = 'Draft') =>
      examBody({
        status,
        config: {
          totalTimeSeconds: null,
          contentProtection,
          resultReleaseMode: 'Instant',
          resultReleaseTime: null,
          markingScheme: { correctMarks: 1, incorrectMarks: 0, unattemptedMarks: 0 },
        },
      });
    const card = (root: HTMLElement) => root.querySelector('app-exam-content-protection') as HTMLElement;
    const box = (root: HTMLElement) => card(root).querySelector('input[type="checkbox"]') as HTMLInputElement;

    it('shows protection as on, which is the default, and also when an older API does not say', () => {
      expect(box(open(withProtection(true)).root).checked).toBe(true);
      expect(box(open(withProtection(undefined)).root).checked).toBe(true);
      expect(box(open(withProtection(false)).root).checked).toBe(false);
    });

    it('sends the new choice, then shows what the server stored', () => {
      const { fixture, root } = open(withProtection(true));

      box(root).click();
      fixture.detectChanges();
      button(card(root), 'Save').click();

      const put = httpMock.expectOne(isProtection);
      expect(put.request.body).toEqual({ contentProtection: false });
      const stored = withProtection(false);
      put.flush(stored);
      httpMock.expectOne(isExam).flush(stored);
      fixture.detectChanges();

      expect(box(root).checked).toBe(false);
    });

    it('can be changed after the exam is published, because it changes nothing that is asked or scored', () => {
      const { fixture, root } = open(withProtection(true, 'Published'));

      box(root).click();
      fixture.detectChanges();
      button(card(root), 'Save').click();

      expect(httpMock.expectOne(isProtection).request.body).toEqual({ contentProtection: false });
    });

    it('cannot be changed once the exam is archived', () => {
      const { root } = open(withProtection(true, 'Archived'));

      expect(box(root)).toBeNull();
      expect(card(root).textContent).toContain('turned off during the exam');
    });
  });

  describe('leaving the exam page (FR-22)', () => {
    const isLimit = (r: { method: string; url: string }) => r.method === 'PUT' && r.url.endsWith('/v1/exams/exam-1/focus-violation-limit');
    const withLimit = (focusViolationLimit: number | undefined, status = 'Draft') =>
      examBody({
        status,
        config: {
          totalTimeSeconds: null,
          focusViolationLimit,
          resultReleaseMode: 'Instant',
          resultReleaseTime: null,
          markingScheme: { correctMarks: 1, incorrectMarks: 0, unattemptedMarks: 0 },
        },
      });
    const card = (root: HTMLElement) => root.querySelector('app-exam-focus-violation-limit') as HTMLElement;
    const box = (root: HTMLElement) => card(root)?.querySelector('input[type="checkbox"]') as HTMLInputElement;

    it('shows the watch as off, which is the default, and also when an older API does not say', () => {
      expect(box(open(withLimit(0)).root).checked).toBe(false);
      expect(box(open(withLimit(undefined)).root).checked).toBe(false);
      expect(box(open(withLimit(3)).root).checked).toBe(true);
    });

    it('sends the new limit, then shows what the server stored', () => {
      const { fixture, root } = open(withLimit(0));

      box(root).click();
      fixture.detectChanges();
      button(card(root), 'Save').click();

      const put = httpMock.expectOne(isLimit);
      expect(put.request.body).toEqual({ focusViolationLimit: 3 });
      const stored = withLimit(3);
      put.flush(stored);
      httpMock.expectOne(isExam).flush(stored);
      fixture.detectChanges();

      expect(box(root).checked).toBe(true);
    });

    it('can be changed after the exam is published, because it changes nothing that is asked or scored', () => {
      const { fixture, root } = open(withLimit(0, 'Published'));

      box(root).click();
      fixture.detectChanges();
      button(card(root), 'Save').click();

      expect(httpMock.expectOne(isLimit).request.body).toEqual({ focusViolationLimit: 3 });
    });

    it('cannot be changed once the exam is archived', () => {
      const { root } = open(withLimit(3, 'Archived'));

      expect(box(root)).toBeNull();
      expect(card(root).textContent).toContain('the attempt ends after 3 times');
    });
  });

  describe('preview as a candidate (FR-15)', () => {
    it.each(['Draft', 'Published', 'Archived'])('links to the preview of a %s exam', (status) => {
      const { root } = open(examBody({ status }));

      const link = Array.from(root.querySelectorAll('a')).find((a) => a.textContent?.includes('Preview as a candidate'));

      expect(link?.getAttribute('href')).toBe('/exams/exam-1/preview');
    });
  });
});
