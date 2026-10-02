import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { AttemptReviewDto } from '../candidate.models';
import { AttemptReview } from './attempt-review';

describe('AttemptReview', () => {
  let httpMock: HttpTestingController;

  // One question answered rightly, one wrongly, one skipped, with negative marking so the marks differ.
  const review = (overrides: Partial<AttemptReviewDto> = {}): AttemptReviewDto => ({
    attemptId: 'a1',
    examId: 'e1',
    examName: 'Maths Final',
    submittedAtUtc: '2026-10-05T04:50:00Z',
    autoSubmitted: false,
    score: 2.75,
    maxScore: 12,
    correctCount: 1,
    wrongCount: 1,
    unansweredCount: 1,
    sections: [
      {
        id: 's1',
        name: 'Section A',
        questions: [
          {
            id: 'q1',
            text: 'What is 2 + 2?',
            verdict: 'Correct',
            marks: 4,
            options: [
              { id: 'o1', text: '4', isCorrect: true, wasChosen: true },
              { id: 'o2', text: '5', isCorrect: false, wasChosen: false },
            ],
          },
          {
            id: 'q2',
            text: 'Capital of France?',
            verdict: 'Wrong',
            marks: -1,
            options: [
              { id: 'o3', text: 'Paris', isCorrect: true, wasChosen: false },
              { id: 'o4', text: 'Rome', isCorrect: false, wasChosen: true },
            ],
          },
        ],
      },
      {
        id: 's2',
        name: 'Section B',
        questions: [
          {
            id: 'q3',
            text: 'Largest planet?',
            verdict: 'Unanswered',
            marks: -0.25,
            options: [
              { id: 'o5', text: 'Jupiter', isCorrect: true, wasChosen: false },
              { id: 'o6', text: 'Mars', isCorrect: false, wasChosen: false },
            ],
          },
        ],
      },
    ],
    ...overrides,
  });

  const root = (fixture: ComponentFixture<AttemptReview>) => fixture.nativeElement as HTMLElement;
  const textOf = (fixture: ComponentFixture<AttemptReview>) => root(fixture).textContent ?? '';
  const card = (fixture: ComponentFixture<AttemptReview>, n: number) => root(fixture).querySelector(`#review-question-${n}`) as HTMLElement;

  async function open(body: AttemptReviewDto | { error: object; status: number }) {
    await TestBed.configureTestingModule({
      imports: [AttemptReview],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ attemptId: 'a1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

    const fixture = TestBed.createComponent(AttemptReview);
    fixture.detectChanges();
    const request = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/review') && r.method === 'GET');
    if ('error' in body) {
      request.flush(body.error, { status: body.status, statusText: 'Conflict' });
    } else {
      request.flush(body);
    }
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => httpMock.verify());

  it('shows the score and how many answers were right, wrong and left out', async () => {
    const fixture = await open(review());

    expect(textOf(fixture)).toContain('Maths Final');
    expect(textOf(fixture)).toContain('2.75 / 12');
    expect(textOf(fixture)).toContain('1 correct');
    expect(textOf(fixture)).toContain('1 wrong');
    expect(textOf(fixture)).toContain('1 not answered');
  });

  it('numbers the questions across sections and keeps each section heading', async () => {
    const fixture = await open(review());

    expect(textOf(fixture)).toContain('Section A');
    expect(textOf(fixture)).toContain('Section B');
    expect(card(fixture, 1).textContent).toContain('What is 2 + 2?');
    expect(card(fixture, 2).textContent).toContain('Capital of France?');
    expect(card(fixture, 3).textContent).toContain('Largest planet?');
  });

  it('marks a right answer as the candidate’s own and correct', async () => {
    const fixture = await open(review());

    const options = card(fixture, 1).querySelectorAll('.review-option');
    expect(options[0].classList).toContain('review-option--correct');
    expect(options[0].textContent).toContain('Your answer · Correct');
    expect(options[1].classList).not.toContain('review-option--correct');
    expect(options[1].classList).not.toContain('review-option--wrong');
    expect(card(fixture, 1).textContent).toContain('+4');
  });

  it('marks a wrong answer as the candidate’s, and shows the option that was right', async () => {
    const fixture = await open(review());

    const [paris, rome] = Array.from(card(fixture, 2).querySelectorAll('.review-option'));
    expect(paris.classList).toContain('review-option--correct');
    expect(paris.textContent).toContain('Correct answer');
    expect(rome.classList).toContain('review-option--wrong');
    expect(rome.textContent).toContain('Your answer');
    expect(rome.textContent).not.toContain('Correct');
    expect(card(fixture, 2).textContent).toContain('Wrong');
    expect(card(fixture, 2).textContent).toContain('-1');
  });

  it('says so for a question left unanswered, and still shows the right option and its marks', async () => {
    const fixture = await open(review());

    expect(card(fixture, 3).textContent).toContain('You did not answer this question.');
    expect(card(fixture, 3).textContent).toContain('Correct answer');
    expect(card(fixture, 3).textContent).toContain('-0.25');
    expect(card(fixture, 3).querySelector('.review-option--wrong')).toBeNull();
  });

  it('colours the jump strip by verdict, with the words in its labels for screen readers', async () => {
    const fixture = await open(review());

    const items = Array.from(root(fixture).querySelectorAll<HTMLButtonElement>('.review-strip__item'));
    expect(items.map((i) => i.getAttribute('aria-label'))).toEqual(['Question 1, Correct', 'Question 2, Wrong', 'Question 3, Not answered']);
    expect(items[0].classList).toContain('review-strip__item--correct');
    expect(items[1].classList).toContain('review-strip__item--wrong');
  });

  it('mentions an automatic submission when time ran out', async () => {
    const fixture = await open(review({ autoSubmitted: true }));

    expect(textOf(fixture)).toContain('Time ran out');
  });

  it('shows formatting in the question text and runs nothing hidden in it', async () => {
    const w = window as unknown as { __reviewRan?: boolean };
    const hostile = review();
    hostile.sections[0].questions[0].text = '<p><strong>Bold</strong></p><img src="x" onerror="window.__reviewRan = true"><script>window.__reviewRan = true</script>';

    const fixture = await open(hostile);

    const text = card(fixture, 1).querySelector('.rich-text') as HTMLElement;
    expect(text.querySelector('strong')?.textContent).toBe('Bold');
    expect(text.innerHTML).not.toContain('onerror');
    expect(text.querySelector('script')).toBeNull();
    expect(w.__reviewRan).toBeUndefined();
  });

  it('shows the reason when the answers have not been released yet, and no answer key', async () => {
    const fixture = await open({
      status: 409,
      error: { title: 'results_not_released', detail: 'The correct answers have not been released yet. They will be shown from 2026-10-08 09:00 UTC.' },
    });

    expect(textOf(fixture)).toContain('have not been released yet');
    expect(root(fixture).querySelector('.review-option')).toBeNull();
  });
});
