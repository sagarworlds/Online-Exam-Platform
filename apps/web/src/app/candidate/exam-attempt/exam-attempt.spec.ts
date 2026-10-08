import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { afterEach, beforeEach, vi } from 'vitest';
import { AttemptDto, AttemptStatusDto } from '../candidate.models';
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
    localStorage.clear();
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    // Tests that move the clock a long way also run the page's heartbeat; what it asked is not what those tests are about.
    httpMock.match((r) => r.method === 'GET' && r.url.endsWith('/status'));
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

  describe('a question with several correct answers', () => {
    const multi = (selected: string[] = []) =>
      attempt({
        sections: [
          {
            id: 's1',
            name: 'Section A',
            questions: [
              {
                id: 'q1',
                text: 'Which are prime?',
                options: [
                  { id: 'o1', text: '2' },
                  { id: 'o2', text: '3' },
                  { id: 'o3', text: '4' },
                ],
                selectedOptionId: selected[0] ?? null,
                selectedOptionIds: selected,
                allowsMultiple: true,
                markedForReview: false,
              },
            ],
          },
        ],
      });
    const boxes = (fixture: ComponentFixture<ExamAttempt>) =>
      Array.from(root(fixture).querySelectorAll<HTMLInputElement>('input[type="checkbox"]'));

    it('offers a checkbox for each option, says to choose all that apply, and shows what was saved', async () => {
      const fixture = await open(multi(['o1', 'o2']));

      expect(radios(fixture)).toHaveLength(0);
      expect(boxes(fixture).map((b) => b.checked)).toEqual([true, true, false]);
      expect(textOf(fixture)).toContain('Choose all the answers that apply.');
      expect(textOf(fixture)).toContain('1 of 1 answered');
    });

    it('saves the whole set each time an option is ticked', async () => {
      const fixture = await open(multi(['o1']));

      boxes(fixture)[1].click();
      fixture.detectChanges();
      const save = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/answers/q1'));

      expect(save.request.method).toBe('PUT');
      expect(save.request.body).toEqual({ optionIds: ['o1', 'o2'] });
      save.flush(null);
      expect(boxes(fixture).map((b) => b.checked)).toEqual([true, true, false]);
    });

    it('saves the set without an option that was unticked', async () => {
      const fixture = await open(multi(['o1', 'o2']));

      boxes(fixture)[0].click();
      fixture.detectChanges();

      const save = httpMock.expectOne((r) => r.url.endsWith('/answers/q1'));
      expect(save.request.body).toEqual({ optionIds: ['o2'] });
      save.flush(null);
    });

    it('takes the answer back when the last option is unticked, which is the same as clearing it', async () => {
      const fixture = await open(multi(['o1']));

      boxes(fixture)[0].click();
      fixture.detectChanges();

      const call = httpMock.expectOne((r) => r.url.endsWith('/answers/q1'));
      expect(call.request.method).toBe('DELETE');
      call.flush(null, { status: 204, statusText: 'No Content' });
      fixture.detectChanges();
      expect(textOf(fixture)).toContain('0 of 1 answered');
    });

    it('puts the previous set back and says so when a save fails', async () => {
      const fixture = await open(multi(['o1']));

      boxes(fixture)[2].click();
      fixture.detectChanges();
      expect(boxes(fixture).map((b) => b.checked)).toEqual([true, false, true]);
      httpMock.expectOne((r) => r.url.endsWith('/answers/q1')).flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(boxes(fixture).map((b) => b.checked)).toEqual([true, false, false]);
      expect(textOf(fixture)).toContain('could not be saved');
    });

    it('clears every chosen option with Clear response', async () => {
      const fixture = await open(multi(['o1', 'o2']));

      (buttonLabelled(fixture, 'Clear response') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(boxes(fixture).every((b) => !b.checked)).toBe(true);
      httpMock.expectOne((r) => r.url.endsWith('/answers/q1') && r.method === 'DELETE').flush(null, { status: 204, statusText: 'No Content' });
    });
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

    it('does not mention unopened questions when every one has been opened', async () => {
      // The first question is on screen (so seen) and the second is answered: nothing is unvisited.
      const fixture = await open(attempt());
      buttonLabelled(fixture, 'Submit exam')?.click();
      fixture.detectChanges();
      expect(textOf(fixture)).not.toContain('never opened');
    });

    it('counts questions never opened, and lists each section when there are several', async () => {
      const many = attempt({
        sections: [
          { id: 's1', name: 'Section A', questions: ['q1', 'q2', 'q3'].map((id) => ({ id, text: id, options: [{ id: `${id}-a`, text: 'A' }], selectedOptionId: null, markedForReview: false })) },
          { id: 's2', name: 'Section B', questions: ['q4', 'q5'].map((id) => ({ id, text: id, options: [{ id: `${id}-a`, text: 'A' }], selectedOptionId: id === 'q5' ? `${id}-a` : null, markedForReview: id === 'q4' })) },
        ],
      });
      const fixture = await open(many);

      buttonLabelled(fixture, 'Submit exam')?.click();
      fixture.detectChanges();

      // q1 is on screen; q2 and q3 were never opened; q4 is marked and q5 answered.
      expect(textOf(fixture)).toContain('2 questions were never opened.');
      const lines = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.submit-summary li')).map((li) => li.textContent?.replace(/\s+/g, ' ').trim());
      expect(lines).toEqual([
        'Section A: 0 of 3 answered, 2 not visited',
        'Section B: 1 of 2 answered, 1 marked for review',
      ]);
    });

    it('shows no per-section list for an exam with one section', async () => {
      const fixture = await open(attempt());
      buttonLabelled(fixture, 'Submit exam')?.click();
      fixture.detectChanges();

      expect((fixture.nativeElement as HTMLElement).querySelector('.submit-summary')).toBeNull();
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

    it('remembers which questions were visited when the page is reloaded', async () => {
      const threeQuestions = attempt({
        sections: [{
          id: 's1',
          name: 'Section A',
          questions: ['q1', 'q2', 'q3'].map((id) => ({
            id, text: id, options: [{ id: `${id}-a`, text: 'A' }], selectedOptionId: null, markedForReview: false,
          })),
        }],
      });
      const first = await open(threeQuestions);
      buttonLabelled(first, 'Next')?.click();
      first.detectChanges();
      first.destroy();
      httpMock.verify();
      TestBed.resetTestingModule();

      const reloaded = await open(threeQuestions);

      expect(labels(reloaded)).toEqual(['Question 1, not answered', 'Question 2, not answered', 'Question 3, not visited']);
    });

    it('treats a damaged remembered value as nothing visited', async () => {
      localStorage.setItem('exam.visited.a1', '{not json');
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

      const fixture = await open(attempt());

      expect(labels(fixture)).toEqual(['Question 1, not answered', 'Question 2, answered']);
      expect(warn).toHaveBeenCalled();
      warn.mockRestore();
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

  it('waits for an answer still being saved before it submits, so the answer is in the score', async () => {
    const fixture = await open(attempt());
    radios(fixture)[1].click();
    const save = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/answers/q1'));

    buttonLabelled(fixture, 'Submit exam')?.click();
    fixture.detectChanges();
    buttonLabelled(fixture, 'Yes, submit')?.click();

    // The save has not been answered yet, so nothing is sent: the server could score before it had the answer.
    httpMock.expectNone((r) => r.url.endsWith('/submit'));
    save.flush(null);
    const submit = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/submit'));
    submit.flush(attempt({ status: 'Submitted', score: 2, maxScore: 2, sections: [], submittedAtUtc: '2026-10-05T04:40:00Z' }));
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('2 / 2');
  });

  it('waits for every answer being saved, not just the last', async () => {
    const fixture = await open(attempt());
    radios(fixture)[1].click();
    const first = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/answers/q1'));
    radios(fixture)[0].click();
    const second = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/answers/q1'));
    buttonLabelled(fixture, 'Submit exam')?.click();
    fixture.detectChanges();
    buttonLabelled(fixture, 'Yes, submit')?.click();

    first.flush(null);
    httpMock.expectNone((r) => r.url.endsWith('/submit'));
    second.flush(null);

    httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/submit')).flush(attempt({ status: 'Submitted', score: 1, maxScore: 2, sections: [] }));
  });

  it('sends a submit again, once, when it clashed with another request for the attempt', async () => {
    const fixture = await open(attempt());
    buttonLabelled(fixture, 'Submit exam')?.click();
    fixture.detectChanges();
    buttonLabelled(fixture, 'Yes, submit')?.click();

    httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/submit'))
      .flush({ title: 'concurrency_conflict', detail: 'Another request changed this at the same moment.' }, { status: 409, statusText: 'Conflict' });
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/submit'))
      .flush(attempt({ status: 'Submitted', score: 1, maxScore: 2, sections: [], submittedAtUtc: '2026-10-05T04:40:00Z' }));
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('1 / 2');
  });

  it('does not keep retrying a submit that keeps being refused', async () => {
    const fixture = await open(attempt());
    buttonLabelled(fixture, 'Submit exam')?.click();
    fixture.detectChanges();
    buttonLabelled(fixture, 'Yes, submit')?.click();

    const conflict = { title: 'concurrency_conflict', detail: 'Still clashing.' };
    httpMock.expectOne((r) => r.url.endsWith('/submit')).flush(conflict, { status: 409, statusText: 'Conflict' });
    httpMock.expectOne((r) => r.url.endsWith('/submit')).flush(conflict, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    httpMock.expectNone((r) => r.url.endsWith('/submit'));
    expect(textOf(fixture)).toContain('Still clashing.');
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

  describe('an accommodation (FR-49)', () => {
    const accommodated = (alternateFormats: ('large_text' | 'high_contrast' | 'screen_reader')[]) =>
      attempt({ accommodation: { extraTimeSeconds: 1800, readerScribe: false, alternateFormats } });
    const zoomOf = (fixture: ComponentFixture<ExamAttempt>) => root(fixture).querySelector<HTMLElement>('.exam-question')!.style.getPropertyValue('--exam-zoom');

    it('starts the page in large text and high contrast when the candidate was given them, without storing it as their choice', async () => {
      const fixture = await open(accommodated(['large_text', 'high_contrast']));

      expect(zoomOf(fixture)).toBe('1.5');
      expect(root(fixture).querySelector('.page--contrast')).not.toBeNull();
      expect(localStorage.getItem('exam.textZoom')).toBeNull();
      expect(localStorage.getItem('exam.highContrast')).toBeNull();
    });

    it('leaves the page as it was for a candidate with no accommodation', async () => {
      const fixture = await open(attempt());

      expect(zoomOf(fixture)).toBe('1');
      expect(root(fixture).querySelector('.page--contrast')).toBeNull();
    });

    it('applies only the formats that were given', async () => {
      const fixture = await open(accommodated(['high_contrast']));

      expect(zoomOf(fixture)).toBe('1');
      expect(root(fixture).querySelector('.page--contrast')).not.toBeNull();
    });

    it('lets the candidate change either, and the change is remembered like any other', async () => {
      const fixture = await open(accommodated(['large_text', 'high_contrast']));

      buttonLabelled(fixture, 'High contrast')?.click();
      (root(fixture).querySelector('button[aria-label="Smaller text"]') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(root(fixture).querySelector('.page--contrast')).toBeNull();
      expect(zoomOf(fixture)).toBe('1.3');
      expect(localStorage.getItem('exam.highContrast')).toBe('off');
      expect(localStorage.getItem('exam.textZoom')).toBe('1.3');
    });

    it('leaves alone a choice the candidate already made on this device', async () => {
      localStorage.setItem('exam.textZoom', '1');
      localStorage.setItem('exam.highContrast', 'off');

      const fixture = await open(accommodated(['large_text', 'high_contrast']));

      expect(zoomOf(fixture)).toBe('1');
      expect(root(fixture).querySelector('.page--contrast')).toBeNull();
    });

    it('does not start in a format for a result that is not an open attempt', async () => {
      const fixture = await open(attempt({ status: 'Submitted', sections: [], accommodation: { extraTimeSeconds: 0, readerScribe: false, alternateFormats: ['high_contrast'] } }));

      expect(root(fixture).querySelector('.page--contrast')).toBeNull();
    });
  });

  describe('with sections locked', () => {
    const lockedAttempt = (activeSectionId = 's1') =>
      attempt({
        sectionLockEnabled: true,
        activeSectionId,
        sections: [
          {
            id: 's1',
            name: 'Section A',
            questions: [
              { id: 'q1', text: 'One', options: [{ id: 'o1', text: 'x' }], selectedOptionId: null, markedForReview: false },
              { id: 'q2', text: 'Two', options: [{ id: 'o2', text: 'y' }], selectedOptionId: null, markedForReview: false },
            ],
          },
          {
            id: 's2',
            name: 'Section B',
            questions: [{ id: 'q3', text: 'Three', options: [{ id: 'o3', text: 'z' }], selectedOptionId: null, markedForReview: false }],
          },
        ],
      });
    const paletteButton = (fixture: ComponentFixture<ExamAttempt>, number: number) =>
      Array.from(root(fixture).querySelectorAll<HTMLButtonElement>('.palette__item')).find((b) => b.textContent?.trim() === String(number))!;

    it('asks before leaving the section, then moves on and closes the one left', async () => {
      const fixture = await open(lockedAttempt());
      buttonLabelled(fixture, 'Save & Next')!.click();
      fixture.detectChanges();
      expect(textOf(fixture)).toContain('Question 2 of 3');
      expect(buttonLabelled(fixture, 'Save & Next section')).toBeTruthy();

      buttonLabelled(fixture, 'Save & Next section')!.click();
      fixture.detectChanges();
      expect(textOf(fixture)).toContain('You will not be able to come back to this section');
      expect(textOf(fixture)).toContain('Question 2 of 3');

      buttonLabelled(fixture, 'Yes, move on')!.click();
      const request = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/section/s2') && r.method === 'PUT');
      request.flush(null, { status: 204, statusText: 'No Content' });
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('Section B · Question 3 of 3');
      expect(paletteButton(fixture, 1).disabled).toBe(true);
      expect(paletteButton(fixture, 2).disabled).toBe(true);
      expect(buttonLabelled(fixture, 'Previous')!.disabled).toBe(true);
    });

    it('stays put when the candidate chooses to stay', async () => {
      const fixture = await open(lockedAttempt());
      paletteButton(fixture, 3).click();
      fixture.detectChanges();
      buttonLabelled(fixture, 'Stay here')!.click();
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('Section A · Question 1 of 3');
      expect(textOf(fixture)).not.toContain('You will not be able to come back');
    });

    it('opens in the section the server says the candidate is in, after a reload', async () => {
      const fixture = await open(lockedAttempt('s2'));

      expect(textOf(fixture)).toContain('Section B · Question 3 of 3');
      expect(paletteButton(fixture, 1).disabled).toBe(true);
    });

    it('leaves every section open when sections are not locked', async () => {
      const fixture = await open(attempt());
      expect(Array.from(root(fixture).querySelectorAll<HTMLButtonElement>('.palette__item')).some((b) => b.disabled)).toBe(false);
    });
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

  describe('display options and keyboard shortcuts', () => {
    const press = (fixture: ComponentFixture<ExamAttempt>, key: string, init: KeyboardEventInit = {}) => {
      document.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init }));
      fixture.detectChanges();
    };
    const position = (fixture: ComponentFixture<ExamAttempt>) => textOf(fixture).match(/Question (\d+) of/)?.[1];

    it('moves between questions with N and P', async () => {
      const fixture = await open(attempt());

      press(fixture, 'n');
      expect(position(fixture)).toBe('2');

      press(fixture, 'p');
      expect(position(fixture)).toBe('1');
    });

    it('ignores a letter pressed with Ctrl held, so browser shortcuts keep working', async () => {
      const fixture = await open(attempt());

      press(fixture, 'n', { ctrlKey: true });

      expect(position(fixture)).toBe('1');
    });

    it('marks a question for review with M', async () => {
      const fixture = await open(attempt());

      press(fixture, 'm');

      const request = httpMock.expectOne((r) => r.url.includes('/marks/q1'));
      request.flush(null);
      fixture.detectChanges();
      expect(buttonLabelled(fixture, 'Mark for review')?.getAttribute('aria-pressed')).toBe('true');
    });

    it('stands down while the submit confirmation is open', async () => {
      const fixture = await open(attempt());
      buttonLabelled(fixture, 'Submit exam')?.click();
      fixture.detectChanges();

      press(fixture, 'n');

      expect(position(fixture)).toBe('1');
    });

    it('turns high contrast on and remembers it for the next visit', async () => {
      const fixture = await open(attempt());
      expect(root(fixture).querySelector('.page--contrast')).toBeNull();

      buttonLabelled(fixture, 'High contrast')?.click();
      fixture.detectChanges();

      expect(root(fixture).querySelector('.page--contrast')).not.toBeNull();
      expect(buttonLabelled(fixture, 'High contrast')?.getAttribute('aria-pressed')).toBe('true');
      expect(localStorage.getItem('exam.highContrast')).toBe('on');
    });
  });

  describe('copy, paste, right-click and print protection (FR-23)', () => {
    /** Fires an event at the page and reports whether it was let through. */
    function fire(type: string): boolean {
      const event = new Event(type, { bubbles: true, cancelable: true });
      document.body.dispatchEvent(event);
      return !event.defaultPrevented;
    }

    const notice = (fixture: ComponentFixture<ExamAttempt>) => root(fixture).querySelector('.exam-protection-notice')?.textContent?.trim();

    it('turns off copying, pasting and the right-click menu while the exam is open, and says so up front', async () => {
      const fixture = await open(attempt());

      expect(fire('copy')).toBe(false);
      expect(fire('paste')).toBe(false);
      expect(fire('contextmenu')).toBe(false);
      expect(textOf(fixture)).toContain('Copying, pasting, right-click and printing are turned off during this exam.');
      expect(document.body.classList).toContain('exam-protected');
    });

    it('says what was just refused, politely, and lets the sentence go after a few seconds', async () => {
      const fixture = await open(attempt());
      const live = root(fixture).querySelector('.exam-protection-notice') as HTMLElement;
      expect(live.getAttribute('role')).toBe('status');
      expect(live.getAttribute('aria-live')).toBe('polite');

      fire('copy');
      fixture.detectChanges();
      expect(notice(fixture)).toBe('Copying is turned off during this exam.');

      vi.advanceTimersByTime(4000);
      fixture.detectChanges();
      expect(notice(fixture)).toBe('');
    });

    it('refuses the keyboard chords but leaves the exam shortcuts and the answer choices alone', async () => {
      const fixture = await open(attempt());

      const chord = new KeyboardEvent('keydown', { key: 'c', ctrlKey: true, bubbles: true, cancelable: true });
      document.body.dispatchEvent(chord);
      expect(chord.defaultPrevented).toBe(true);

      const next = new KeyboardEvent('keydown', { key: 'n', bubbles: true, cancelable: true });
      document.body.dispatchEvent(next);
      fixture.detectChanges();
      expect(textOf(fixture)).toContain('Question 2 of 2');
      expect(radios(fixture).length).toBeGreaterThan(0);
    });

    it('does nothing when the author has lifted the protection', async () => {
      const fixture = await open(attempt({ contentProtection: false }));

      expect(fire('copy')).toBe(true);
      expect(fire('contextmenu')).toBe(true);
      expect(textOf(fixture)).not.toContain('turned off during this exam');
      expect(document.body.classList).not.toContain('exam-protected');
    });

    it('is on when an older API does not say, since protection is the default', async () => {
      const older = attempt();
      delete older.contentProtection;
      await open(older);

      expect(fire('copy')).toBe(false);
    });

    it('leaves the candidate free to copy and print their own result once the exam is over', async () => {
      const fixture = await open(attempt({ status: 'Submitted', score: 1, maxScore: 2, sections: [] }));

      expect(fire('copy')).toBe(true);
      expect(fire('contextmenu')).toBe(true);
      expect(textOf(fixture)).not.toContain('turned off during this exam');
    });

    it('lets go when the exam is submitted, without a reload', async () => {
      const fixture = await open(attempt());
      expect(fire('copy')).toBe(false);

      (fixture.componentInstance as unknown as { attempt: { set(value: AttemptDto): void } }).attempt.set(
        attempt({ status: 'Submitted', score: 1, maxScore: 2, sections: [] }),
      );
      fixture.detectChanges();

      expect(fire('copy')).toBe(true);
      expect(document.body.classList).not.toContain('exam-protected');
    });

    it('hands everything back when the page is left', async () => {
      const fixture = await open(attempt());
      expect(fire('copy')).toBe(false);

      fixture.destroy();

      expect(fire('copy')).toBe(true);
      expect(fire('contextmenu')).toBe(true);
      expect(document.body.classList).not.toContain('exam-protected');
    });
  });

  describe('leaving the exam page (FR-22)', () => {
    const isReport = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/me/attempts/a1/focus-violations');
    const warning = (fixture: ComponentFixture<ExamAttempt>) => root(fixture).querySelector('.exam-focus-warning');
    const leave = () => window.dispatchEvent(new Event('blur'));
    const comeBack = () => window.dispatchEvent(new Event('focus'));
    const withFullscreen = async (run: () => Promise<void>) => {
      Object.defineProperty(document, 'fullscreenEnabled', { configurable: true, value: true });
      try {
        await run();
      } finally {
        delete (document as unknown as Record<string, unknown>)['fullscreenEnabled'];
      }
    };

    it('says up front that leaving is recorded, with the count so far and the limit', async () => {
      const fixture = await open(attempt({ focusViolationLimit: 3, focusViolations: 1 }));

      expect(textOf(fixture)).toContain('you have left 1 of 3 times allowed');
    });

    it('says nothing, and reports nothing, when the exam does not watch', async () => {
      const fixture = await open(attempt());

      leave();
      fixture.detectChanges();

      httpMock.expectNone(isReport);
      expect(textOf(fixture)).not.toContain('Leaving it, or leaving full screen');
    });

    it('reports a departure to the server, then warns with how many are left, until dismissed', async () => {
      const fixture = await open(attempt({ focusViolationLimit: 3 }));

      leave();
      const report = httpMock.expectOne(isReport);
      expect(report.request.body).toEqual({ kind: 'WindowBlurred' });
      report.flush({ violations: 1, limit: 3, attemptEnded: false });
      fixture.detectChanges();

      expect(warning(fixture)?.getAttribute('role')).toBe('alert');
      expect(warning(fixture)?.textContent).toContain('You left the exam page (1 of 3 allowed). If you leave 2 more times, the exam will be submitted.');
      expect(textOf(fixture)).toContain('you have left 1 of 3 times allowed');

      (warning(fixture)?.querySelector('button') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(warning(fixture)).toBeNull();
    });

    it('says "once more" on the last warning', async () => {
      const fixture = await open(attempt({ focusViolationLimit: 3, focusViolations: 1 }));

      leave();
      httpMock.expectOne(isReport).flush({ violations: 2, limit: 3, attemptEnded: false });
      fixture.detectChanges();

      expect(warning(fixture)?.textContent).toContain('If you leave once more, the exam will be submitted.');
    });

    it('counts the next trip away only after the candidate is back', async () => {
      await open(attempt({ focusViolationLimit: 5 }));

      leave();
      leave();
      httpMock.expectOne(isReport).flush({ violations: 1, limit: 5, attemptEnded: false });
      comeBack();
      leave();

      httpMock.expectOne(isReport).flush({ violations: 2, limit: 5, attemptEnded: false });
    });

    it('shows the result when the departure reached the limit and the server ended the attempt', async () => {
      const fixture = await open(attempt({ focusViolationLimit: 2, focusViolations: 1 }));

      leave();
      httpMock.expectOne(isReport).flush({ violations: 2, limit: 2, attemptEnded: true });
      httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1') && r.method === 'GET').flush(
        attempt({ status: 'Submitted', score: 1, maxScore: 2, sections: [], autoSubmitted: true, endedByViolations: true, focusViolationLimit: 2, focusViolations: 2 }),
      );
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('1 / 2');
      expect(textOf(fixture)).toContain('You left the exam page too many times, so the exam was submitted automatically');
      expect(textOf(fixture)).not.toContain('Time ran out');
      expect(warning(fixture)).toBeNull();
    });

    it('reloads the attempt when the server says it is already over', async () => {
      const fixture = await open(attempt({ focusViolationLimit: 3 }));

      leave();
      httpMock.expectOne(isReport).flush({ title: 'attempt_not_in_progress' }, { status: 409, statusText: 'Conflict' });
      httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1') && r.method === 'GET').flush(
        attempt({ status: 'Submitted', score: 0, maxScore: 2, sections: [], autoSubmitted: true }),
      );
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('Time ran out');
    });

    it('drops a report that could not be sent, without an error on the page', async () => {
      const fixture = await open(attempt({ focusViolationLimit: 3 }));

      leave();
      httpMock.expectOne(isReport).error(new ProgressEvent('error'));
      fixture.detectChanges();

      expect(root(fixture).querySelector('.error-message')).toBeNull();
      expect(warning(fixture)).toBeNull();
    });

    it('stops watching once the exam is submitted', async () => {
      const fixture = await open(attempt({ focusViolationLimit: 3 }));
      (fixture.componentInstance as unknown as { attempt: { set(value: AttemptDto): void } }).attempt.set(
        attempt({ status: 'Submitted', score: 1, maxScore: 2, sections: [], focusViolationLimit: 3 }),
      );
      fixture.detectChanges();

      leave();

      httpMock.expectNone(isReport);
    });

    it('stops watching when the page is left', async () => {
      const fixture = await open(attempt({ focusViolationLimit: 3 }));

      fixture.destroy();
      leave();

      httpMock.expectNone(isReport);
    });

    it('offers full screen while the exam watches and the browser allows it', () =>
      withFullscreen(async () => {
        const fixture = await open(attempt({ focusViolationLimit: 3 }));

        expect(buttonLabelled(fixture, 'Enter full screen')).toBeDefined();
      }));

    it('does not offer full screen when the exam does not watch', () =>
      withFullscreen(async () => {
        const fixture = await open(attempt());

        expect(buttonLabelled(fixture, 'Enter full screen')).toBeUndefined();
      }));

    it('asks the browser for full screen when the candidate presses the button', () =>
      withFullscreen(async () => {
        const request = vi.fn().mockResolvedValue(undefined);
        document.documentElement.requestFullscreen = request;
        const fixture = await open(attempt({ focusViolationLimit: 3 }));

        (buttonLabelled(fixture, 'Enter full screen') as HTMLButtonElement).click();

        expect(request).toHaveBeenCalledTimes(1);
      }));
  });

  describe('organiser actions (FR-29)', () => {
    const isStatus = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/me/attempts/a1/status');
    const isAttempt = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/me/attempts/a1');
    const status = (overrides: Partial<AttemptStatusDto> = {}): AttemptStatusDto => ({
      status: 'InProgress',
      pausedAtUtc: null,
      deadlineUtc: '2026-10-05T05:00:00Z',
      serverTimeUtc: new Date(Date.now()).toISOString(),
      warnings: [],
      ...overrides,
    });
    const beat = (fixture: ComponentFixture<ExamAttempt>, answer: AttemptStatusDto) => {
      vi.advanceTimersByTime(10_000);
      // The server's clock is read when it answers, so it is stamped after the time has moved.
      httpMock.expectOne(isStatus).flush({ ...answer, serverTimeUtc: new Date(Date.now()).toISOString() });
      fixture.detectChanges();
    };
    const warnings = (fixture: ComponentFixture<ExamAttempt>) => Array.from(root(fixture).querySelectorAll('.exam-focus-warning'));

    beforeEach(() => sessionStorage.clear());

    it('asks the server how the attempt stands every ten seconds, quietly', async () => {
      const fixture = await open(attempt());

      beat(fixture, status());
      beat(fixture, status());

      expect(textOf(fixture)).toContain('Question 1 of 2');
    });

    it('offers to report an issue during a sitting, naming the attempt and the question on screen (FR-42)', async () => {
      const fixture = await open(attempt());

      const report = Array.from(root(fixture).querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Report an issue') as HTMLButtonElement;
      expect(report).toBeTruthy();
      report.click();
      fixture.detectChanges();
      const box = root(fixture).querySelector('textarea') as HTMLTextAreaElement;
      box.value = 'Option C is missing';
      box.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      (Array.from(root(fixture).querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Send report') as HTMLButtonElement).click();

      const request = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/me/attempts/a1/issues'));
      expect(request.request.body).toMatchObject({ category: 'Question', message: 'Option C is missing' });
      expect((request.request.body as { questionId: string }).questionId).toBeTruthy();
      request.flush({ id: 'r1', category: 'Question', questionId: null, message: 'Option C is missing', reportedAtUtc: '2026-10-05T04:31:00Z' });
    });

    it('still offers to report an issue while an organiser has the attempt paused', async () => {
      const fixture = await open(attempt());

      beat(fixture, status({ pausedAtUtc: new Date(Date.now()).toISOString() }));

      expect(textOf(fixture)).toContain('Your exam is paused');
      expect(Array.from(root(fixture).querySelectorAll('button')).some((b) => b.textContent?.trim() === 'Report an issue')).toBe(true);
    });

    it('hides the questions and says so when an organiser pauses the attempt, and brings them back on resume with the new deadline', async () => {
      const fixture = await open(attempt());

      beat(fixture, status({ pausedAtUtc: new Date(Date.now()).toISOString() }));
      expect(textOf(fixture)).toContain('Your exam is paused');
      expect(textOf(fixture)).not.toContain('What is 2 + 2?');

      beat(fixture, status({ deadlineUtc: '2026-10-05T05:10:00Z' }));
      expect(textOf(fixture)).not.toContain('Your exam is paused');
      expect(textOf(fixture)).toContain('What is 2 + 2?');
      expect(root(fixture).querySelector('.countdown')?.textContent?.trim()).toBe('39:40');
    });

    it('stops the countdown while paused', async () => {
      const fixture = await open(attempt());
      beat(fixture, status({ pausedAtUtc: new Date(Date.now()).toISOString() }));
      const frozen = root(fixture).querySelector('.countdown')?.textContent?.trim();

      vi.advanceTimersByTime(5_000);
      fixture.detectChanges();

      expect(root(fixture).querySelector('.countdown')?.textContent?.trim()).toBe(frozen);
      httpMock.expectNone(isAttempt);
    });

    it('does not count stepping away as a departure while paused', async () => {
      const fixture = await open(attempt({ focusViolationLimit: 3 }));
      beat(fixture, status({ pausedAtUtc: new Date(Date.now()).toISOString() }));

      window.dispatchEvent(new Event('blur'));

      httpMock.expectNone((r) => r.url.endsWith('/focus-violations'));
    });

    it('shows a warning an organiser sent as an alert, until the candidate dismisses it', async () => {
      const fixture = await open(attempt());

      beat(fixture, status({ warnings: [{ id: 'w1', message: 'Eyes on your own screen.', issuedAtUtc: '2026-10-05T04:40:00Z' }] }));

      expect(warnings(fixture)).toHaveLength(1);
      expect(warnings(fixture)[0].getAttribute('role')).toBe('alert');
      expect(warnings(fixture)[0].textContent).toContain('Eyes on your own screen.');

      (warnings(fixture)[0].querySelector('button') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(warnings(fixture)).toHaveLength(0);

      // The same warning on the next heartbeat is not shown again.
      beat(fixture, status({ warnings: [{ id: 'w1', message: 'Eyes on your own screen.', issuedAtUtc: '2026-10-05T04:40:00Z' }] }));
      expect(warnings(fixture)).toHaveLength(0);
    });

    it('does not show a dismissed warning again after a reload, but shows one it has not seen', async () => {
      sessionStorage.setItem('exam.dismissedWarnings.a1', JSON.stringify(['w1']));
      const fixture = await open(
        attempt({
          warnings: [
            { id: 'w1', message: 'Seen already.', issuedAtUtc: '2026-10-05T04:40:00Z' },
            { id: 'w2', message: 'Not seen yet.', issuedAtUtc: '2026-10-05T04:41:00Z' },
          ],
        }),
      );

      expect(warnings(fixture)).toHaveLength(1);
      expect(warnings(fixture)[0].textContent).toContain('Not seen yet.');
    });

    it('loads the result when an organiser ends the attempt, and says why', async () => {
      const fixture = await open(attempt());

      vi.advanceTimersByTime(10_000);
      httpMock.expectOne(isStatus).flush(status({ status: 'Submitted' }));
      httpMock.expectOne(isAttempt).flush(
        attempt({ status: 'Submitted', score: 1, maxScore: 2, sections: [], autoSubmitted: true, terminatedByAdmin: true, terminationReason: 'Caught using a phone.' }),
      );
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('1 / 2');
      expect(textOf(fixture)).toContain('An organiser ended this exam.');
      expect(textOf(fixture)).toContain('Caught using a phone.');
      expect(textOf(fixture)).not.toContain('Time ran out');
    });

    it('shows an invalidated result with its reason instead of a score, and no review link', async () => {
      const fixture = await open(
        attempt({
          status: 'Submitted',
          score: null,
          maxScore: null,
          sections: [],
          invalidated: true,
          invalidationReason: 'Answers were shared.',
          review: { available: true, availableFromUtc: null, mode: 'Instant' },
        }),
      );

      expect(textOf(fixture)).toContain('This result was invalidated by an organiser, so it has no score. Reason: Answers were shared.');
      expect(root(fixture).querySelector('.score')).toBeNull();
      expect(textOf(fixture)).not.toContain('Review answers');
      expect(textOf(fixture)).not.toContain('Your answers have been marked');
    });

    it('stops asking once the exam is submitted, and when the page is left', async () => {
      const fixture = await open(attempt());
      (fixture.componentInstance as unknown as { attempt: { set(value: AttemptDto): void } }).attempt.set(
        attempt({ status: 'Submitted', score: 1, maxScore: 2, sections: [] }),
      );
      fixture.detectChanges();

      vi.advanceTimersByTime(30_000);

      httpMock.expectNone(isStatus);
    });

    it('stops asking when the page is left', async () => {
      const fixture = await open(attempt());

      fixture.destroy();
      vi.advanceTimersByTime(30_000);

      httpMock.expectNone(isStatus);
    });

    it('ignores a heartbeat that fails, and tries again at the next one', async () => {
      const fixture = await open(attempt());

      vi.advanceTimersByTime(10_000);
      httpMock.expectOne(isStatus).error(new ProgressEvent('error'));
      fixture.detectChanges();
      expect(root(fixture).querySelector('.error-message')).toBeNull();

      beat(fixture, status({ pausedAtUtc: new Date(Date.now()).toISOString() }));
      expect(textOf(fixture)).toContain('Your exam is paused');
    });
  });
});
