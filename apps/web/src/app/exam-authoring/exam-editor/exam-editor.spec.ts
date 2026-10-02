import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { ExamEditor } from './exam-editor';

const isExam = (r: { method: string; url: string }) => r.method === 'GET' && /\/v1\/exams\/exam-1$/.test(r.url);
const isBank = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions');

const question = (id: string, text: string) => ({
  id,
  text,
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

  it('shows a published exam read-only', () => {
    const { root } = open(examBody({ status: 'Published', isScheduled: true, scheduledStartTime: '2026-10-05T04:30:00Z', scheduledEndTime: '2026-10-05T07:30:00Z' }));

    expect(root.querySelector('form[aria-label="New section"]')).toBeNull();
    expect(button(root, 'Add question')).toBeUndefined();
    expect(root.textContent).toContain('no longer be edited');
  });
});
