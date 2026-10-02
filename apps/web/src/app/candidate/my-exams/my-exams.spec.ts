import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { MyExams } from './my-exams';

describe('MyExams', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MyExams],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const exam = (overrides: Record<string, unknown>) => ({
    examId: 'e1',
    name: 'Maths Final',
    description: null,
    startUtc: '2026-10-05T04:30:00Z',
    endUtc: '2026-10-05T07:30:00Z',
    lateEntryDeadlineUtc: null,
    durationSeconds: 5400,
    questionCount: 20,
    state: 'Open',
    attemptId: null,
    attemptStatus: null,
    score: null,
    maxScore: null,
    ...overrides,
  });

  it('lists the exams with their state, size and duration', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush([
      exam({}),
      exam({ examId: 'e2', name: 'Physics', state: 'NotOpen', durationSeconds: null, questionCount: 5 }),
      exam({ examId: 'e3', name: 'Chemistry', state: 'Closed' }),
    ]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Maths Final');
    expect(text).toContain('20 questions, 90 minutes');
    expect(text).toContain('Not open yet');
    expect(text).toContain('Closed');
    expect(text).toContain('5 questions');
  });

  it('tells a candidate with no exams how to get one', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush([]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('invitation e-mail');
  });

  it('offers a Start button only for an open exam that has no attempt yet', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush([
      exam({ name: 'Open one' }),
      exam({ examId: 'e2', name: 'Future one', state: 'NotOpen' }),
      exam({ examId: 'e3', name: 'Closed one', state: 'Closed' }),
    ]);
    fixture.detectChanges();

    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('button');
    expect(buttons.length).toBe(1);
    expect(buttons[0].textContent).toContain('Start exam');
  });

  it('starts the attempt and opens it when Start is pressed', () => {
    const fixture = TestBed.createComponent(MyExams);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush([exam({})]);
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector('button')?.click();
    const start = httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams/e1/attempts'));
    expect(start.request.method).toBe('POST');
    start.flush({ id: 'attempt-9' });

    expect(navigate).toHaveBeenCalledWith(['/attempt', 'attempt-9']);
  });

  it('shows the error when the exam cannot be started', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush([exam({})]);
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector('button')?.click();
    httpMock
      .expectOne((r) => r.url.endsWith('/v1/me/exams/e1/attempts'))
      .flush({ title: 'exam_closed', detail: 'This exam is closed; it can no longer be started.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('This exam is closed');
  });

  it('offers to resume an attempt in progress', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush([exam({ attemptId: 'a1', attemptStatus: 'InProgress' })]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('button')).toBeNull();
    const link = Array.from(root.querySelectorAll('a')).find((a) => a.textContent?.includes('Resume exam'));
    expect(link?.getAttribute('href')).toBe('/attempt/a1');
  });

  it('shows the score of a submitted attempt with a link to the result', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    httpMock
      .expectOne((r) => r.url.endsWith('/v1/me/exams'))
      .flush([exam({ attemptId: 'a1', attemptStatus: 'Submitted', score: 14, maxScore: 20 })]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('14 / 20');
    expect(root.querySelector('button')).toBeNull();
    const link = Array.from(root.querySelectorAll('a')).find((a) => a.textContent?.includes('View result'));
    expect(link?.getAttribute('href')).toBe('/attempt/a1');
  });
});
