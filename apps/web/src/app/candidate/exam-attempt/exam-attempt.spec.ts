import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { afterEach, beforeEach, vi } from 'vitest';
import { AttemptDto } from '../candidate.models';
import { ExamAttempt } from './exam-attempt';

describe('ExamAttempt', () => {
  const NOW = Date.parse('2026-10-05T04:30:00Z');
  let httpMock: HttpTestingController;

  const attempt = (overrides: Partial<AttemptDto> = {}): AttemptDto => ({
    id: 'a1',
    examId: 'e1',
    examName: 'Maths Final',
    status: 'InProgress',
    startedAtUtc: '2026-10-05T04:30:00Z',
    deadlineUtc: '2026-10-05T05:00:00Z',
    submittedAtUtc: null,
    autoSubmitted: false,
    score: null,
    maxScore: null,
    serverTimeUtc: '2026-10-05T04:30:00Z',
    sections: [
      {
        id: 's1',
        name: 'Section A',
        questions: [
          {
            id: 'q1',
            text: 'What is 2 + 2?',
            options: [
              { id: 'o1', text: '4' },
              { id: 'o2', text: '5' },
            ],
            selectedOptionId: null,
          },
          {
            id: 'q2',
            text: 'Capital of France?',
            options: [
              { id: 'o3', text: 'Paris' },
              { id: 'o4', text: 'Rome' },
            ],
            selectedOptionId: 'o3',
          },
        ],
      },
    ],
    ...overrides,
  });

  const root = (fixture: ComponentFixture<ExamAttempt>) => fixture.nativeElement as HTMLElement;
  const textOf = (fixture: ComponentFixture<ExamAttempt>) => root(fixture).textContent ?? '';

  async function open(initial: AttemptDto) {
    await TestBed.configureTestingModule({
      imports: [ExamAttempt],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ attemptId: 'a1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

    const fixture = TestBed.createComponent(ExamAttempt);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1') && r.method === 'GET').flush(initial);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('shows the questions with the answers saved so far, and no hint of which option is right', async () => {
    const fixture = await open(attempt());

    expect(textOf(fixture)).toContain('Maths Final');
    expect(textOf(fixture)).toContain('1. What is 2 + 2?');
    expect(textOf(fixture)).toContain('1 of 2 answered');
    const radios = Array.from(root(fixture).querySelectorAll<HTMLInputElement>('input[type="radio"]'));
    expect(radios.map((r) => r.checked)).toEqual([false, false, true, false]);
  });

  it('counts down to the deadline the server set, using the server clock rather than the candidate clock', async () => {
    // The candidate's clock is 10 minutes slow; the server says it is 04:30:00 and the deadline is 05:00:00.
    vi.setSystemTime(NOW - 10 * 60_000);
    const fixture = await open(attempt());
    expect(textOf(fixture)).toContain('Time left: 30:00');

    vi.advanceTimersByTime(65_000);
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('Time left: 28:55');
  });

  it('saves a choice as it is made', async () => {
    const fixture = await open(attempt());

    root(fixture).querySelectorAll<HTMLInputElement>('input[type="radio"]')[1].click();
    const save = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/answers/q1'));
    expect(save.request.method).toBe('PUT');
    expect(save.request.body).toEqual({ optionId: 'o2' });
    save.flush(null);
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('2 of 2 answered');
  });

  it('puts the previous choice back and says so when a save fails', async () => {
    const fixture = await open(attempt());

    // q2 was Paris (o3); picking Rome (o4) shows at once, then the save fails.
    root(fixture).querySelectorAll<HTMLInputElement>('input[type="radio"]')[3].click();
    fixture.detectChanges();
    expect(root(fixture).querySelectorAll<HTMLInputElement>('input[type="radio"]')[3].checked).toBe(true);
    httpMock.expectOne((r) => r.url.endsWith('/answers/q2')).flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const radios = Array.from(root(fixture).querySelectorAll<HTMLInputElement>('input[type="radio"]'));
    expect(radios.map((r) => r.checked)).toEqual([false, false, true, false]);
    expect(textOf(fixture)).toContain('could not be saved');
  });

  it('asks before submitting, then shows the score', async () => {
    const fixture = await open(attempt());

    root(fixture).querySelector<HTMLButtonElement>('button.primary')?.click();
    fixture.detectChanges();
    expect(textOf(fixture)).toContain('Submit now? 1 of 2 questions are answered');
    httpMock.expectNone((r) => r.url.endsWith('/submit'));

    Array.from(root(fixture).querySelectorAll('button')).find((b) => b.textContent?.includes('Yes, submit'))?.click();
    const submit = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/submit'));
    expect(submit.request.method).toBe('POST');
    submit.flush(attempt({ status: 'Submitted', score: 1, maxScore: 2, sections: [], submittedAtUtc: '2026-10-05T04:40:00Z' }));
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('1 / 2');
    expect(root(fixture).querySelector('input[type="radio"]')).toBeNull();
    expect(root(fixture).querySelector('a')?.getAttribute('href')).toBe('/my-exams');
  });

  it('lets the candidate back out of submitting', async () => {
    const fixture = await open(attempt());

    root(fixture).querySelector<HTMLButtonElement>('button.primary')?.click();
    fixture.detectChanges();
    Array.from(root(fixture).querySelectorAll('button')).find((b) => b.textContent?.includes('Keep working'))?.click();
    fixture.detectChanges();

    expect(textOf(fixture)).not.toContain('Submit now?');
    expect(textOf(fixture)).toContain('Submit exam');
  });

  it('reloads the attempt when time runs out, which the server closes and scores', async () => {
    const fixture = await open(attempt());

    vi.advanceTimersByTime(30 * 60_000 + 1_000);
    const reload = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1') && r.method === 'GET');
    reload.flush(attempt({ status: 'Submitted', autoSubmitted: true, score: 1, maxScore: 2, sections: [] }));
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('1 / 2');
    expect(textOf(fixture)).toContain('Time ran out');
  });

  it('reloads when a save is refused because the attempt has just ended', async () => {
    const fixture = await open(attempt());

    root(fixture).querySelectorAll<HTMLInputElement>('input[type="radio"]')[0].click();
    httpMock
      .expectOne((r) => r.url.endsWith('/answers/q1'))
      .flush({ title: 'attempt_not_in_progress', detail: 'This attempt has already been submitted.' }, { status: 409, statusText: 'Conflict' });
    httpMock
      .expectOne((r) => r.url.endsWith('/v1/me/attempts/a1') && r.method === 'GET')
      .flush(attempt({ status: 'Submitted', score: 1, maxScore: 2, sections: [] }));
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('1 / 2');
  });

  it('shows the result of an attempt that is already submitted', async () => {
    const fixture = await open(attempt({ status: 'Submitted', score: 7, maxScore: 10, sections: [] }));

    expect(textOf(fixture)).toContain('7 / 10');
    expect(textOf(fixture)).not.toContain('Time left');
  });

  it('shows why an attempt cannot be opened', async () => {
    await TestBed.configureTestingModule({
      imports: [ExamAttempt],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ attemptId: 'a1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ExamAttempt);
    fixture.detectChanges();

    httpMock
      .expectOne((r) => r.url.endsWith('/v1/me/attempts/a1'))
      .flush({ title: 'attempt_not_found', detail: 'No attempt matches the given id.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('No attempt matches the given id.');
  });
});
