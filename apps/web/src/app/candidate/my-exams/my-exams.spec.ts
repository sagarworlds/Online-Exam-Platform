import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { AttemptSummaryDto } from '../candidate.models';
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

  const attempt = (number: number, status: 'InProgress' | 'Submitted', score: number | null = null): AttemptSummaryDto => ({
    id: `a${number}`,
    number,
    status,
    startedAtUtc: '2026-10-05T04:30:00Z',
    submittedAtUtc: status === 'Submitted' ? '2026-10-05T04:50:00Z' : null,
    autoSubmitted: false,
    score,
    maxScore: score === null ? null : 20,
  });

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
    attemptsAllowed: 1,
    attemptsUsed: 0,
    // The window is open and nothing has been started, which is what the default exam is.
    canStartAttempt: true,
    attempts: [],
    ...overrides,
  });

  it('lists the exams with their state, size and duration', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush([
      exam({}),
      exam({ examId: 'e2', name: 'Physics', state: 'NotOpen', durationSeconds: null, questionCount: 5, canStartAttempt: false }),
      exam({ examId: 'e3', name: 'Chemistry', state: 'Closed', canStartAttempt: false }),
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
      exam({ examId: 'e2', name: 'Future one', state: 'NotOpen', canStartAttempt: false }),
      exam({ examId: 'e3', name: 'Closed one', state: 'Closed', canStartAttempt: false }),
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
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush([exam({ attemptId: 'a1', attemptStatus: 'InProgress', attemptsUsed: 1, canStartAttempt: false, attempts: [attempt(1, 'InProgress')] })]);
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
      .flush([
        exam({ attemptId: 'a1', attemptStatus: 'Submitted', score: 14, maxScore: 20, attemptsUsed: 1, canStartAttempt: false, attempts: [attempt(1, 'Submitted', 14)] }),
      ]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('14 / 20');
    expect(root.querySelector('button')).toBeNull();
    const link = Array.from(root.querySelectorAll('a')).find((a) => a.textContent?.includes('View result'));
    expect(link?.getAttribute('href')).toBe('/attempt/a1');
  });
  describe('several attempts', () => {
    const twoAttempts = (overrides: Record<string, unknown> = {}) =>
      exam({
        attemptId: 'a2',
        attemptStatus: 'Submitted',
        score: 18,
        maxScore: 20,
        attemptsAllowed: 2,
        attemptsUsed: 2,
        canStartAttempt: false,
        attempts: [attempt(1, 'Submitted', 11), attempt(2, 'Submitted', 18)],
        ...overrides,
      });

    function open(exams: unknown[]) {
      const fixture = TestBed.createComponent(MyExams);
      fixture.detectChanges();
      httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush(exams);
      fixture.detectChanges();
      return { fixture, root: fixture.nativeElement as HTMLElement };
    }

    const rows = (root: HTMLElement) => Array.from(root.querySelectorAll<HTMLElement>('.attempt-row'));

    it('lists each attempt with its number and score, and says how many are used of how many allowed', () => {
      const { root } = open([twoAttempts()]);

      expect(root.textContent).toContain('Attempts used: 2 of 2');
      expect(rows(root).map((r) => r.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
        expect.stringContaining('Attempt 1 · Your score: 11 / 20'),
        expect.stringContaining('Attempt 2 · Your score: 18 / 20'),
      ]);
      expect(rows(root).map((r) => r.querySelector('a')?.getAttribute('href'))).toEqual(['/attempt/a1', '/attempt/a2']);
    });

    it('marks the best attempt, and only once there are two to compare', () => {
      const { root } = open([twoAttempts(), exam({ examId: 'e2', name: 'Single', attemptsUsed: 1, canStartAttempt: false, attempts: [attempt(1, 'Submitted', 15)] })]);

      const [multi, single] = Array.from(root.querySelectorAll('.card'));
      expect(multi.querySelectorAll('.badge--active').length).toBe(1 + 1); // the "Open" state badge and "Best"
      expect(rows(multi as HTMLElement)[1].textContent).toContain('Best');
      expect(rows(multi as HTMLElement)[0].textContent).not.toContain('Best');
      expect(single.textContent).not.toContain('Best');
    });

    it('does not number the attempts of an exam that has only ever had one', () => {
      const { root } = open([exam({ attemptsUsed: 1, canStartAttempt: false, attempts: [attempt(1, 'Submitted', 15)] })]);

      expect(root.textContent).not.toContain('Attempts used');
      expect(root.textContent).not.toContain('Attempt 1');
      expect(root.textContent).toContain('Your score: 15 / 20');
    });

    it('offers the next attempt once an administrator has given one, labelled with its number', () => {
      const { root } = open([
        twoAttempts({ attemptsAllowed: 2, attemptsUsed: 1, canStartAttempt: true, attempts: [attempt(1, 'Submitted', 11)], attemptId: 'a1', score: 11 }),
      ]);

      const buttons = root.querySelectorAll('button');
      expect(buttons.length).toBe(1);
      expect(buttons[0].textContent).toContain('Start attempt 2');
    });

    it('offers a resume, not a new attempt, while one is open', () => {
      const { root } = open([
        twoAttempts({ attemptStatus: 'InProgress', score: null, attemptsUsed: 2, canStartAttempt: false, attempts: [attempt(1, 'Submitted', 11), attempt(2, 'InProgress')] }),
      ]);

      expect(root.querySelector('button')).toBeNull();
      expect(rows(root)[1].textContent).toContain('In progress');
      expect(rows(root)[1].querySelector('a')?.textContent).toContain('Resume exam');
    });

    it('starts the next attempt and opens it', () => {
      const fixture = TestBed.createComponent(MyExams);
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      fixture.detectChanges();
      httpMock
        .expectOne((r) => r.url.endsWith('/v1/me/exams'))
        .flush([twoAttempts({ attemptsUsed: 1, canStartAttempt: true, attempts: [attempt(1, 'Submitted', 11)] })]);
      fixture.detectChanges();

      (fixture.nativeElement as HTMLElement).querySelector('button')?.click();
      httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams/e1/attempts') && r.method === 'POST').flush({ id: 'attempt-2' });

      expect(navigate).toHaveBeenCalledWith(['/attempt', 'attempt-2']);
    });
  });
});
