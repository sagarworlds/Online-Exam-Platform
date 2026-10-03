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
            markedForReview: false,
          },
          {
            id: 'q2',
            text: 'Capital of France?',
            options: [
              { id: 'o3', text: 'Paris' },
              { id: 'o4', text: 'Rome' },
            ],
            selectedOptionId: 'o3',
            markedForReview: false,
          },
        ],
      },
    ],
    ...overrides,
  });

  const root = (fixture: ComponentFixture<ExamAttempt>) => fixture.nativeElement as HTMLElement;
  const textOf = (fixture: ComponentFixture<ExamAttempt>) => root(fixture).textContent ?? '';
  const radios = (fixture: ComponentFixture<ExamAttempt>) =>
    Array.from(root(fixture).querySelectorAll<HTMLInputElement>('input[type="radio"]'));
  const buttonLabelled = (fixture: ComponentFixture<ExamAttempt>, label: string) =>
    Array.from(root(fixture).querySelectorAll('button')).find((b) => b.textContent?.includes(label));

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

  it('shows the first question with the answers saved so far, and no hint of which option is right', async () => {
    const fixture = await open(attempt());

    expect(textOf(fixture)).toContain('Maths Final');
    expect(textOf(fixture)).toContain('Section A · Question 1 of 2');
    expect(textOf(fixture)).toContain('What is 2 + 2?');
    expect(textOf(fixture)).not.toContain('Capital of France?');
    expect(textOf(fixture)).toContain('1 of 2 answered');
    expect(radios(fixture).map((r) => r.checked)).toEqual([false, false]);

    buttonLabelled(fixture, 'Next')?.click();
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('Question 2 of 2');
    expect(radios(fixture).map((r) => r.checked)).toEqual([true, false]);
  });

  it('shows formatting in the question text and runs nothing hidden in it', async () => {
    const w = window as unknown as { __ran?: boolean };
    const hostile = attempt();
    hostile.sections[0].questions[0].text =
      '<p>Pick the <strong>bigger</strong> number: H<sub>2</sub>O</p>' +
      '<script>window.__ran = true</script><img src="x" onerror="window.__ran = true">';
    const fixture = await open(hostile);

    const text = root(fixture).querySelector('.question__text') as HTMLElement;
    expect(text.querySelector('strong')?.textContent).toBe('bigger');
    expect(text.querySelector('sub')?.textContent).toBe('2');
    expect(root(fixture).querySelector('script')).toBeNull();
    expect(root(fixture).querySelector('[onerror]')).toBeNull();
    expect(w.__ran).toBeUndefined();
  });

  it('names the answer group after the question text, for screen readers', async () => {
    const fixture = await open(attempt());

    const fieldset = root(fixture).querySelector('fieldset.question') as HTMLElement;
    const label = root(fixture).querySelector(`#${fieldset.getAttribute('aria-labelledby')}`) as HTMLElement;
    expect(label.textContent).toContain('What is 2 + 2?');
  });

  it('moves between questions with Previous and Next, and disables each at the ends of the exam', async () => {
    const fixture = await open(attempt());
    expect((buttonLabelled(fixture, 'Previous') as HTMLButtonElement).disabled).toBe(true);
    expect((buttonLabelled(fixture, 'Next') as HTMLButtonElement).disabled).toBe(false);

    buttonLabelled(fixture, 'Next')?.click();
    fixture.detectChanges();
    expect((buttonLabelled(fixture, 'Next') as HTMLButtonElement).disabled).toBe(true);

    buttonLabelled(fixture, 'Previous')?.click();
    fixture.detectChanges();
    expect(textOf(fixture)).toContain('Question 1 of 2');
  });

  it('numbers every question in a palette, marks the answered ones, and jumps to the one pressed', async () => {
    const fixture = await open(attempt());

    const items = Array.from(root(fixture).querySelectorAll<HTMLButtonElement>('.palette__item'));
    expect(items.map((b) => b.textContent?.trim())).toEqual(['1', '2']);
    expect(items.map((b) => b.classList.contains('palette__item--answered'))).toEqual([false, true]);
    expect(items[0].getAttribute('aria-current')).toBe('true');

    items[1].click();
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('Capital of France?');
    expect(root(fixture).querySelectorAll('.palette__item')[1].getAttribute('aria-current')).toBe('true');
  });

  it('counts down to the deadline the server set, using the server clock rather than the candidate clock', async () => {
    // The candidate's clock is 10 minutes slow; the server says it is 04:30:00 and the deadline is 05:00:00.
    vi.setSystemTime(NOW - 10 * 60_000);
    const fixture = await open(attempt());
    const countdown = () => root(fixture).querySelector('.countdown')?.textContent?.trim();
    expect(countdown()).toBe('30:00');

    vi.advanceTimersByTime(65_000);
    fixture.detectChanges();

    expect(countdown()).toBe('28:55');
  });

  it('saves a choice as it is made', async () => {
    const fixture = await open(attempt());

    radios(fixture)[1].click();
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
    buttonLabelled(fixture, 'Next')?.click();
    fixture.detectChanges();
    radios(fixture)[1].click();
    fixture.detectChanges();
    expect(radios(fixture)[1].checked).toBe(true);
    httpMock.expectOne((r) => r.url.endsWith('/answers/q2')).flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(radios(fixture).map((r) => r.checked)).toEqual([true, false]);
    expect(textOf(fixture)).toContain('could not be saved');
  });

  describe('clearing a response', () => {
    const clearButton = (fixture: ComponentFixture<ExamAttempt>) => buttonLabelled(fixture, 'Clear response') as HTMLButtonElement;

    it('is only on offer for a question that has an answer', async () => {
      const fixture = await open(attempt());
      expect(clearButton(fixture).disabled).toBe(true); // q1 has none

      buttonLabelled(fixture, 'Next')?.click();
      fixture.detectChanges();

      expect(clearButton(fixture).disabled).toBe(false); // q2 is answered
    });

    it('takes the answer back at once and tells the server', async () => {
      const fixture = await open(attempt());
      buttonLabelled(fixture, 'Next')?.click();
      fixture.detectChanges();

      clearButton(fixture).click();
      fixture.detectChanges();

      expect(radios(fixture).map((r) => r.checked)).toEqual([false, false]);
      expect(textOf(fixture)).toContain('0 of 2 answered');
      const call = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/answers/q2'));
      expect(call.request.method).toBe('DELETE');
      call.flush(null, { status: 204, statusText: 'No Content' });
      fixture.detectChanges();
      expect(clearButton(fixture).disabled).toBe(true);
    });

    it('puts the answer back and says so when the server could not clear it', async () => {
      const fixture = await open(attempt());
      buttonLabelled(fixture, 'Next')?.click();
      fixture.detectChanges();

      clearButton(fixture).click();
      httpMock.expectOne((r) => r.url.endsWith('/answers/q2')).flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(radios(fixture).map((r) => r.checked)).toEqual([true, false]);
      expect(textOf(fixture)).toContain('could not be cleared');
    });

    it('reloads when the attempt has just ended, as a refused save does', async () => {
      const fixture = await open(attempt());
      buttonLabelled(fixture, 'Next')?.click();
      fixture.detectChanges();

      clearButton(fixture).click();
      httpMock
        .expectOne((r) => r.url.endsWith('/answers/q2'))
        .flush({ title: 'attempt_not_in_progress', detail: 'This attempt has already been submitted.' }, { status: 409, statusText: 'Conflict' });
      httpMock
        .expectOne((r) => r.url.endsWith('/v1/me/attempts/a1') && r.method === 'GET')
        .flush(attempt({ status: 'Submitted', score: 1, maxScore: 2, sections: [] }));
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('1 / 2');
    });
  });

  describe('marking for review', () => {
    const markButton = (fixture: ComponentFixture<ExamAttempt>) => buttonLabelled(fixture, 'Mark for review') as HTMLButtonElement;
    const palette = (fixture: ComponentFixture<ExamAttempt>) => Array.from(root(fixture).querySelectorAll<HTMLButtonElement>('.palette__item'));

    it('marks the question at once, tells the server, and shows it in the palette and the count', async () => {
      const fixture = await open(attempt());
      expect(markButton(fixture).getAttribute('aria-pressed')).toBe('false');

      markButton(fixture).click();
      fixture.detectChanges();

      expect(markButton(fixture).getAttribute('aria-pressed')).toBe('true');
      expect(palette(fixture)[0].classList.contains('palette__item--marked')).toBe(true);
      expect(textOf(fixture)).toContain('1 of 2 answered · 1 marked for review');
      const call = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/marks/q1'));
      expect(call.request.method).toBe('PUT');
      call.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('takes the mark off again with a second press', async () => {
      const marked = attempt();
      marked.sections[0].questions[0].markedForReview = true;
      const fixture = await open(marked);
      expect(markButton(fixture).getAttribute('aria-pressed')).toBe('true');

      markButton(fixture).click();
      fixture.detectChanges();

      expect(markButton(fixture).getAttribute('aria-pressed')).toBe('false');
      expect(palette(fixture)[0].classList.contains('palette__item--marked')).toBe(false);
      const call = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/marks/q1'));
      expect(call.request.method).toBe('DELETE');
      call.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('puts the mark back as it was and says so when the server could not save it', async () => {
      const fixture = await open(attempt());

      markButton(fixture).click();
      fixture.detectChanges();
      httpMock.expectOne((r) => r.url.endsWith('/marks/q1')).flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(markButton(fixture).getAttribute('aria-pressed')).toBe('false');
      expect(textOf(fixture)).toContain('review mark could not be saved');
    });

    it('does not touch the answer: an answered question can be marked, and stays answered', async () => {
      const fixture = await open(attempt());
      buttonLabelled(fixture, 'Next')?.click();
      fixture.detectChanges();

      markButton(fixture).click();
      fixture.detectChanges();
      httpMock.expectOne((r) => r.url.endsWith('/marks/q2')).flush(null, { status: 204, statusText: 'No Content' });

      expect(radios(fixture).map((r) => r.checked)).toEqual([true, false]);
      expect(textOf(fixture)).toContain('1 of 2 answered');
    });

    it('reloads when the attempt has just ended', async () => {
      const fixture = await open(attempt());

      markButton(fixture).click();
      httpMock
        .expectOne((r) => r.url.endsWith('/marks/q1'))
        .flush({ title: 'attempt_not_in_progress', detail: 'This attempt has already been submitted.' }, { status: 409, statusText: 'Conflict' });
      httpMock
        .expectOne((r) => r.url.endsWith('/v1/me/attempts/a1') && r.method === 'GET')
        .flush(attempt({ status: 'Submitted', score: 1, maxScore: 2, sections: [] }));
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('1 / 2');
    });

    it('says in the submit question how many are marked, so none is forgotten', async () => {
      const marked = attempt();
      marked.sections[0].questions[0].markedForReview = true;
      marked.sections[0].questions[1].markedForReview = true;
      const fixture = await open(marked);

      buttonLabelled(fixture, 'Submit exam')?.click();
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('Submit now? 1 of 2 questions are answered.');
      expect(textOf(fixture)).toContain('2 questions are marked for review.');
    });

    it('says "1 question is" for a single mark, and nothing at all when none is marked', async () => {
      const one = attempt();
      one.sections[0].questions[0].markedForReview = true;
      const fixture = await open(one);
      buttonLabelled(fixture, 'Submit exam')?.click();
      fixture.detectChanges();
      expect(textOf(fixture)).toContain('1 question is marked for review.');
      httpMock.verify();
      TestBed.resetTestingModule();

      const none = await open(attempt());
      buttonLabelled(none, 'Submit exam')?.click();
      none.detectChanges();
      expect(textOf(none)).not.toContain('marked for review');
    });
  });

  describe('the question palette', () => {
    const palette = (fixture: ComponentFixture<ExamAttempt>) => Array.from(root(fixture).querySelectorAll<HTMLButtonElement>('.palette__item'));
    const labels = (fixture: ComponentFixture<ExamAttempt>) => palette(fixture).map((b) => b.getAttribute('aria-label'));

    it('tells a question not yet visited from one seen but not answered, in words as well as colour', async () => {
      const fixture = await open(attempt({
        sections: [{
          id: 's1',
          name: 'Section A',
          questions: ['q1', 'q2', 'q3'].map((id) => ({
            id, text: id, options: [{ id: `${id}-a`, text: 'A' }], selectedOptionId: null, markedForReview: false,
          })),
        }],
      }));
      // The first question is on screen, so it has been seen; the others have not.
      expect(labels(fixture)).toEqual(['Question 1, not answered', 'Question 2, not visited', 'Question 3, not visited']);
      expect(palette(fixture).map((b) => b.classList.contains('palette__item--seen'))).toEqual([true, false, false]);

      buttonLabelled(fixture, 'Next')?.click();
      fixture.detectChanges();

      expect(labels(fixture)).toEqual(['Question 1, not answered', 'Question 2, not answered', 'Question 3, not visited']);
    });

    it('gives each of the five states its own wording', async () => {
      const make = (id: string, selected: string | null, marked: boolean) => ({
        id, text: id, options: [{ id: `${id}-a`, text: 'A' }], selectedOptionId: selected, markedForReview: marked,
      });
      const fixture = await open(attempt({
        sections: [{
          id: 's1',
          name: 'Section A',
          questions: [make('q1', null, false), make('q2', 'q2-a', false), make('q3', null, true), make('q4', 'q4-a', true), make('q5', null, false)],
        }],
      }));

      expect(labels(fixture)).toEqual([
        'Question 1, not answered',
        'Question 2, answered',
        'Question 3, marked for review',
        'Question 4, answered and marked for review',
        'Question 5, not visited',
      ]);
      // Marked and answered keeps the answered look and adds the marked dot; the classes carry both.
      expect(palette(fixture)[3].classList.contains('palette__item--answered')).toBe(true);
      expect(palette(fixture)[3].classList.contains('palette__item--marked')).toBe(true);
    });

    it('lists all of the states in the legend', async () => {
      const fixture = await open(attempt());

      const legend = Array.from(root(fixture).querySelectorAll('.palette-legend li')).map((li) => li.textContent?.trim());

      expect(legend).toEqual(['Answered', 'Not answered', 'Not visited', 'Marked for review', 'Current']);
    });
  });

  it('asks before submitting, then shows the score', async () => {
    const fixture = await open(attempt());

    buttonLabelled(fixture, 'Submit exam')?.click();
    fixture.detectChanges();
    expect(textOf(fixture)).toContain('Submit now? 1 of 2 questions are answered');
    httpMock.expectNone((r) => r.url.endsWith('/submit'));

    buttonLabelled(fixture, 'Yes, submit')?.click();
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

    buttonLabelled(fixture, 'Submit exam')?.click();
    fixture.detectChanges();
    buttonLabelled(fixture, 'Keep working')?.click();
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

    radios(fixture)[0].click();
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

  describe('reviewing the answers', () => {
    const submitted = (review: AttemptDto['review']) => attempt({ status: 'Submitted', score: 7, maxScore: 10, sections: [], review });
    const reviewLink = (fixture: ComponentFixture<ExamAttempt>) =>
      Array.from(root(fixture).querySelectorAll('a')).find((a) => a.textContent?.includes('Review answers'));

    it('offers the review once the answers are released, linking to this attempt', async () => {
      const fixture = await open(submitted({ available: true, mode: 'Instant', availableFromUtc: null }));

      expect(reviewLink(fixture)?.getAttribute('href')).toBe('/attempt/a1/review');
    });

    it('says from when the answers will be shown, for a scheduled release, and offers no link yet', async () => {
      const fixture = await open(submitted({ available: false, mode: 'Scheduled', availableFromUtc: '2026-10-08T09:00:00Z' }));

      expect(textOf(fixture)).toContain('The correct answers will be shown from');
      expect(textOf(fixture)).toContain('2026');
      expect(reviewLink(fixture)).toBeUndefined();
    });

    it('says an administrator will release them, for a manual release', async () => {
      const fixture = await open(submitted({ available: false, mode: 'Manual', availableFromUtc: null }));

      expect(textOf(fixture)).toContain('organiser releases them');
      expect(reviewLink(fixture)).toBeUndefined();
    });

    it('says nothing about a review for an attempt that is still open', async () => {
      const fixture = await open(attempt());

      expect(textOf(fixture)).not.toContain('correct answers');
      expect(reviewLink(fixture)).toBeUndefined();
    });
  });

  it('says which attempt it is on the result and on the exam bar, only from the second on', async () => {
    const first = await open(attempt({ status: 'Submitted', score: 7, maxScore: 10, sections: [], number: 1 }));
    expect(textOf(first)).not.toContain('Attempt 1');
    httpMock.verify();
    TestBed.resetTestingModule();

    const second = await open(attempt({ status: 'Submitted', score: 7, maxScore: 10, sections: [], number: 2 }));
    expect(textOf(second)).toContain('Result · Attempt 2');
    httpMock.verify();
    TestBed.resetTestingModule();

    const open2 = await open(attempt({ number: 2 }));
    expect(textOf(open2)).toContain('Exam · Attempt 2');
  });

  it('moves to the next question on Save & Next and stays put on the last one', async () => {
    const fixture = await open(attempt());
    expect(textOf(fixture)).toContain('Question 1 of 2');

    buttonLabelled(fixture, 'Save & Next')!.click();
    fixture.detectChanges();
    expect(textOf(fixture)).toContain('Question 2 of 2');
    expect(buttonLabelled(fixture, 'Save & Next')!.disabled).toBe(true);
  });

  it('makes the question text larger and smaller, within limits, and remembers the size', async () => {
    localStorage.removeItem('exam.textZoom');
    const fixture = await open(attempt());
    const card = () => root(fixture).querySelector<HTMLElement>('.exam-question')!;

    expect(buttonLabelled(fixture, 'A−')!.disabled).toBe(true);
    for (let i = 0; i < 5; i++) {
      (root(fixture).querySelector('button[aria-label="Larger text"]') as HTMLButtonElement).click();
      fixture.detectChanges();
    }
    expect(card().style.getPropertyValue('--exam-zoom')).toBe('1.5');
    expect((root(fixture).querySelector('button[aria-label="Larger text"]') as HTMLButtonElement).disabled).toBe(true);
    expect(localStorage.getItem('exam.textZoom')).toBe('1.5');

    (root(fixture).querySelector('button[aria-label="Smaller text"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(card().style.getPropertyValue('--exam-zoom')).toBe('1.3');
    localStorage.removeItem('exam.textZoom');
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
