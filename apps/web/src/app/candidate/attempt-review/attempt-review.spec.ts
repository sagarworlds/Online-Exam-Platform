import { formatDate } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { AttemptReviewDto, DisputeWindowDto, MyDisputeDto, ScoreRevisionDto } from '../candidate.models';
import { AttemptReview } from './attempt-review';

describe('AttemptReview', () => {
  let httpMock: HttpTestingController;

  // One question answered rightly, one wrongly, one skipped, with negative marking so the marks differ.
  const review = (overrides: Partial<AttemptReviewDto> = {}): AttemptReviewDto => ({
    attemptId: 'a1',
    examId: 'e1',
    examName: 'Maths Final',
    number: 1,
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

  it('is read in the language the candidate chose, down to the verdicts and the tags on the options', async () => {
    localStorage.clear();
    const fixture = await open(review());

    TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();

    const text = textOf(fixture);
    expect(text).toContain('उत्तरों की समीक्षा');
    expect(text).toContain('1 सही');
    expect(text).toContain('1 गलत');
    expect(text).toContain('1 अनुत्तरित');
    expect(text).toContain('प्रश्न 2');
    expect(card(fixture, 2).textContent).toContain('सही उत्तर');
    expect(card(fixture, 2).textContent).toContain('आपका उत्तर');
    expect(root(fixture).querySelector('.review-strip__item')?.getAttribute('aria-label')).toBe('प्रश्न 1, सही');
    // What the author wrote is not translated by the page.
    expect(text).toContain('Capital of France?');
    localStorage.clear();
  });

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

  it('says which attempt it is only when there is more than one', async () => {
    const first = await open(review());
    expect(textOf(first)).not.toContain('Attempt 1');
    TestBed.resetTestingModule();

    const second = await open(review({ number: 2 }));
    expect(textOf(second)).toContain('Answer review · Attempt 2');
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

  it('shows no revision notice when the score has never been revised', async () => {
    const fixture = await open(review());

    expect(root(fixture).querySelector('.warning-note')).toBeNull();
  });

  it('shows when and why the score changed, once the answer key has been corrected', async () => {
    const fixture = await open(
      review({
        score: 4,
        revisions: [
          {
            previousScore: 2.75,
            previousMaxScore: 12,
            newScore: 4,
            newMaxScore: 12,
            reason: 'Paris is the capital of France, not Rome',
            revisedAtUtc: '2026-10-06T10:00:00Z',
          },
        ],
      }),
    );

    const text = textOf(fixture);
    expect(text).toContain('Your score changed');
    expect(text).toContain('Paris is the capital of France, not Rome');
    expect(text).toContain('2.75');
    expect(text).toContain('4');
  });

  it('shows the reason when the answers have not been released yet, and no answer key', async () => {
    const fixture = await open({
      status: 409,
      error: { title: 'results_not_released', detail: 'The correct answers have not been released yet. They will be shown from 2026-10-08 09:00 UTC.' },
    });

    expect(textOf(fixture)).toContain('have not been released yet');
    expect(root(fixture).querySelector('.review-option')).toBeNull();
  });

  describe('result versions (FR-31)', () => {
    const revision = (overrides: Partial<ScoreRevisionDto> = {}): ScoreRevisionDto => ({
      previousScore: 2.75,
      previousMaxScore: 12,
      newScore: 4,
      newMaxScore: 12,
      reason: 'Paris is the capital of France, not Rome',
      revisedAtUtc: '2026-10-06T10:00:00Z',
      ...overrides,
    });

    it('says nothing about a version while the result is as first submitted', async () => {
      const fixture = await open(review({ resultVersion: 1 }));

      expect(textOf(fixture)).not.toContain('Result version');
    });

    it('says nothing about a version when the API does not number them', async () => {
      const fixture = await open(review());

      expect(textOf(fixture)).not.toContain('Result version');
    });

    it('says which version of the result this is once the score has been revised', async () => {
      const fixture = await open(review({ resultVersion: 2, revisions: [revision({ version: 2 })] }));

      expect(textOf(fixture)).toContain('Result version 2');
    });

    it('numbers each revision by the version of the result it produced', async () => {
      const fixture = await open(
        review({
          resultVersion: 3,
          revisions: [revision({ version: 2 }), revision({ version: 3, previousScore: 4, newScore: 5, reason: 'Second correction', revisedAtUtc: '2026-10-07T10:00:00Z' })],
        }),
      );

      const lines = Array.from(root(fixture).querySelectorAll('.warning-note li')).map((li) => (li.textContent ?? '').replace(/\s+/g, ' ').trim());
      expect(lines).toHaveLength(2);
      expect(lines[0]).toMatch(/^Version 2: 2\.75 \/ 12 → 4 \/ 12 on .* — Paris is the capital of France, not Rome$/);
      expect(lines[1]).toMatch(/^Version 3: 4 \/ 12 → 5 \/ 12 on .* — Second correction$/);
      expect(textOf(fixture)).toContain('Result version 3');
    });

    it('leaves the version off a revision that the API did not number', async () => {
      const fixture = await open(review({ revisions: [revision()] }));

      const line = root(fixture).querySelector('.warning-note li')?.textContent ?? '';
      expect(line).toContain('Paris is the capital of France, not Rome');
      expect(line).not.toContain('Version');
    });
  });

  describe('disputing an answer key (FR-31)', () => {
    const CLOSES = '2026-10-12T09:00:00Z';
    const windowOf = (overrides: Partial<DisputeWindowDto> = {}): DisputeWindowDto => ({ enabled: true, open: true, closesAtUtc: CLOSES, ...overrides });
    const dispute = (overrides: Partial<MyDisputeDto> = {}): MyDisputeDto => ({
      id: 'd1',
      questionId: 'q2',
      reason: 'Paris is the capital of France',
      raisedAtUtc: '2026-10-06T10:00:00Z',
      status: 'Open',
      resolvedAtUtc: null,
      resolutionNote: null,
      ...overrides,
    });

    const disputeButtons = (fixture: ComponentFixture<AttemptReview>) =>
      Array.from(root(fixture).querySelectorAll('button')).filter((b) => b.textContent?.trim() === 'Dispute this answer key');
    const buttonIn = (parent: HTMLElement, label: string) => Array.from(parent.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
    const type = (fixture: ComponentFixture<AttemptReview>, value: string) => {
      const box = root(fixture).querySelector('textarea') as HTMLTextAreaElement;
      box.value = value;
      box.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const isDisputePost = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/me/attempts/a1/disputes');
    /** Opens the form under a question and types a reason in it. */
    const fillIn = (fixture: ComponentFixture<AttemptReview>, questionNumber: number, reason = 'Paris is the capital of France') => {
      buttonIn(card(fixture, questionNumber), 'Dispute this answer key').click();
      fixture.detectChanges();
      type(fixture, reason);
    };

    describe('what the page says about the time allowed', () => {
      it('tells the candidate until when they can dispute while the window is open', async () => {
        const fixture = await open(review({ disputeWindow: windowOf() }));

        expect(textOf(fixture)).toContain(`You can dispute an answer key until ${formatDate(CLOSES, 'medium', 'en-US')}.`);
        expect(textOf(fixture)).not.toContain('has ended');
      });

      it('says the time has ended once the window has closed', async () => {
        const fixture = await open(review({ disputeWindow: windowOf({ open: false }) }));

        expect(textOf(fixture)).toContain('The time to dispute this result has ended.');
        expect(textOf(fixture)).not.toContain('You can dispute');
      });

      it('says nothing about disputes when the platform does not take them', async () => {
        const fixture = await open(review({ disputeWindow: windowOf({ enabled: false, open: false, closesAtUtc: null }) }));

        expect(textOf(fixture).toLowerCase()).not.toContain('dispute');
        expect(disputeButtons(fixture)).toHaveLength(0);
      });

      it('says nothing about disputes when the API gave no window', async () => {
        const fixture = await open(review({ disputeWindow: null, disputes: null }));

        expect(textOf(fixture).toLowerCase()).not.toContain('dispute');
        expect(disputeButtons(fixture)).toHaveLength(0);
      });

      it('says nothing about disputes when the API predates them', async () => {
        const fixture = await open(review());

        expect(textOf(fixture).toLowerCase()).not.toContain('dispute');
        expect(disputeButtons(fixture)).toHaveLength(0);
      });
    });

    describe('who can start one', () => {
      it('offers a dispute under every question while the window is open and nothing was disputed', async () => {
        const fixture = await open(review({ disputeWindow: windowOf(), disputes: [] }));

        expect(disputeButtons(fixture)).toHaveLength(3);
        for (const n of [1, 2, 3]) {
          expect(card(fixture, n).textContent).toContain('Dispute this answer key');
        }
      });

      it('offers none under a question that already has a dispute, which is shown instead', async () => {
        const fixture = await open(review({ disputeWindow: windowOf(), disputes: [dispute()] }));

        expect(disputeButtons(fixture)).toHaveLength(2);
        expect(card(fixture, 2).textContent).not.toContain('Dispute this answer key');
        expect(card(fixture, 2).textContent).toContain('Your dispute');
        expect(card(fixture, 1).textContent).toContain('Dispute this answer key');
        expect(card(fixture, 3).textContent).toContain('Dispute this answer key');
      });

      it('offers none once the window has closed, but still shows the disputes that were raised', async () => {
        const fixture = await open(review({ disputeWindow: windowOf({ open: false }), disputes: [dispute()] }));

        expect(disputeButtons(fixture)).toHaveLength(0);
        expect(card(fixture, 2).textContent).toContain('Paris is the capital of France');
      });

      it('offers none when the platform does not take disputes', async () => {
        const fixture = await open(review({ disputeWindow: windowOf({ enabled: false, open: false }) }));

        expect(disputeButtons(fixture)).toHaveLength(0);
      });
    });

    describe('raising one', () => {
      it('opens a form with a required, limited reason, and sends nothing until it is submitted', async () => {
        const fixture = await open(review({ disputeWindow: windowOf() }));

        buttonIn(card(fixture, 2), 'Dispute this answer key').click();
        fixture.detectChanges();

        const form = card(fixture, 2).querySelector('form') as HTMLFormElement;
        expect(form.getAttribute('aria-label')).toBe('Dispute the answer key of question 2');
        const box = form.querySelector('textarea') as HTMLTextAreaElement;
        expect(form.querySelector('label')?.textContent).toContain('Why do you think the answer key is wrong?');
        expect(form.querySelector('label')?.getAttribute('for')).toBe(box.id);
        expect(box.maxLength).toBe(1000);
        expect(box.getAttribute('aria-required')).toBe('true');
        expect(form.textContent).toContain('Up to 1000 characters.');
        expect(buttonIn(form, 'Send dispute').disabled).toBe(true);
        httpMock.expectNone(isDisputePost);
      });

      it('does not allow sending a reason of only spaces', async () => {
        const fixture = await open(review({ disputeWindow: windowOf() }));
        fillIn(fixture, 2, '    ');

        expect(buttonIn(card(fixture, 2), 'Send dispute').disabled).toBe(true);
        (card(fixture, 2).querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
        httpMock.expectNone(isDisputePost);
      });

      it('sends the trimmed reason for that question and shows the dispute at once, without reading the review again', async () => {
        const fixture = await open(review({ disputeWindow: windowOf(), disputes: [] }));
        fillIn(fixture, 2, '  Paris is the capital of France  ');

        const send = buttonIn(card(fixture, 2), 'Send dispute');
        expect(send.disabled).toBe(false);
        send.click();
        fixture.detectChanges();

        const post = httpMock.expectOne(isDisputePost);
        expect(post.request.body).toEqual({ questionId: 'q2', reason: 'Paris is the capital of France' });
        post.flush(dispute(), { status: 201, statusText: 'Created' });
        fixture.detectChanges();

        httpMock.expectNone((r) => r.method === 'GET');
        const shown = card(fixture, 2);
        expect(shown.querySelector('form')).toBeNull();
        expect(shown.textContent).not.toContain('Dispute this answer key');
        expect(shown.textContent).toContain('Your dispute');
        expect(shown.textContent).toContain('Waiting for staff');
        expect(shown.textContent).toContain('Paris is the capital of France');
        expect(shown.querySelector('[role="status"]')?.textContent).toContain('Your dispute was sent.');
        // The other questions can still be disputed.
        expect(disputeButtons(fixture)).toHaveLength(2);
      });

      it('locks the form while the request runs, so one click sends one dispute', async () => {
        const fixture = await open(review({ disputeWindow: windowOf() }));
        fillIn(fixture, 2);

        const send = buttonIn(card(fixture, 2), 'Send dispute');
        send.click();
        fixture.detectChanges();
        send.click();

        expect(send.disabled).toBe(true);
        expect(buttonIn(card(fixture, 2), 'Cancel').disabled).toBe(true);
        expect((card(fixture, 2).querySelector('textarea') as HTMLTextAreaElement).disabled).toBe(true);
        httpMock.expectOne(isDisputePost).flush(dispute(), { status: 201, statusText: 'Created' });
      });

      it('closes the form on Cancel and sends nothing', async () => {
        const fixture = await open(review({ disputeWindow: windowOf() }));
        fillIn(fixture, 2);

        buttonIn(card(fixture, 2), 'Cancel').click();
        fixture.detectChanges();

        expect(card(fixture, 2).querySelector('form')).toBeNull();
        expect(disputeButtons(fixture)).toHaveLength(3);
        httpMock.expectNone(isDisputePost);
      });

      it('keeps one form open at a time, starting empty', async () => {
        const fixture = await open(review({ disputeWindow: windowOf() }));
        fillIn(fixture, 1, 'Half-written');

        buttonIn(card(fixture, 3), 'Dispute this answer key').click();
        fixture.detectChanges();

        expect(root(fixture).querySelectorAll('form')).toHaveLength(1);
        expect(card(fixture, 3).querySelector('form')).not.toBeNull();
        expect((card(fixture, 3).querySelector('textarea') as HTMLTextAreaElement).value).toBe('');
      });
    });

    describe('when the API refuses', () => {
      async function refused(status: number, body: object) {
        const fixture = await open(review({ disputeWindow: windowOf() }));
        fillIn(fixture, 2);
        buttonIn(card(fixture, 2), 'Send dispute').click();
        fixture.detectChanges();
        httpMock.expectOne(isDisputePost).flush(body, { status, statusText: 'Refused' });
        fixture.detectChanges();
        return fixture;
      }
      const alertIn = (fixture: ComponentFixture<AttemptReview>) => card(fixture, 2).querySelector('[role="alert"]')?.textContent?.trim();

      it('says the question was already disputed, in the page’s own words', async () => {
        const fixture = await refused(409, { title: 'dispute_already_raised', detail: 'A dispute of this question already exists.' });

        expect(alertIn(fixture)).toBe('You have already disputed this question.');
        expect(card(fixture, 2).querySelector('form')).not.toBeNull();
      });

      it('shows the server’s sentence when the time to dispute has passed', async () => {
        const fixture = await refused(409, { title: 'dispute_window_closed', detail: 'The time to dispute this result ended on 2026-10-12 09:00 UTC.' });

        expect(alertIn(fixture)).toBe('The time to dispute this result ended on 2026-10-12 09:00 UTC.');
      });

      it('asks for a reason, within the limit, when the API says it was missing or too long', async () => {
        const fixture = await refused(400, { title: 'invalid_attempt', detail: 'The reason must be 1 to 1000 characters.' });

        expect(alertIn(fixture)).toBe('Say why you think the answer key is wrong, in up to 1000 characters.');
      });

      it('shows the API’s own reason for any other refusal', async () => {
        const fixture = await refused(409, { title: 'attempt_invalidated', detail: 'This result was invalidated.' });

        expect(alertIn(fixture)).toBe('This result was invalidated.');
      });

      it('says it could not be sent when the API gave no reason', async () => {
        const fixture = await refused(500, {});

        expect(alertIn(fixture)).toBe('Your dispute could not be sent. Please try again.');
      });

      it('keeps what was typed, so the candidate can send it again', async () => {
        const fixture = await refused(500, {});

        expect((card(fixture, 2).querySelector('textarea') as HTMLTextAreaElement).value).toBe('Paris is the capital of France');
        buttonIn(card(fixture, 2), 'Send dispute').click();
        fixture.detectChanges();
        httpMock.expectOne(isDisputePost).flush(dispute(), { status: 201, statusText: 'Created' });
        fixture.detectChanges();

        expect(card(fixture, 2).querySelector('[role="alert"]')).toBeNull();
        expect(card(fixture, 2).textContent).toContain('Waiting for staff');
      });
    });

    describe('how a dispute shows under its question', () => {
      const statusOf = (fixture: ComponentFixture<AttemptReview>, n: number) => card(fixture, n).querySelector('.review-dispute__status') as HTMLElement;

      it('says an unanswered dispute is waiting for staff, with no note', async () => {
        const fixture = await open(review({ disputeWindow: windowOf(), disputes: [dispute()] }));

        expect(statusOf(fixture, 2).textContent?.trim()).toBe('Waiting for staff');
        expect(statusOf(fixture, 2).classList).toContain('badge--warning');
        expect(card(fixture, 2).textContent).toContain('Paris is the capital of France');
        expect(card(fixture, 2).textContent).not.toContain('Staff explained');
        expect(card(fixture, 2).textContent).not.toContain('Reason for the correction');
      });

      it('says an accepted dispute corrected the key, with the reason for the correction', async () => {
        const fixture = await open(
          review({ disputeWindow: windowOf({ open: false }), disputes: [dispute({ status: 'Accepted', resolvedAtUtc: '2026-10-06T12:00:00Z', resolutionNote: 'The key had Rome marked' })] }),
        );

        expect(statusOf(fixture, 2).textContent?.trim()).toBe('Accepted — the answer key was corrected');
        expect(statusOf(fixture, 2).classList).toContain('badge--active');
        expect(card(fixture, 2).textContent).toContain('Reason for the correction: The key had Rome marked');
      });

      it('says a rejected dispute left the key as it was, with what staff explained', async () => {
        const fixture = await open(
          review({ disputeWindow: windowOf({ open: false }), disputes: [dispute({ status: 'Rejected', resolvedAtUtc: '2026-10-06T12:00:00Z', resolutionNote: 'Rome was the capital then' })] }),
        );

        expect(statusOf(fixture, 2).textContent?.trim()).toBe('Rejected — the answer key stands');
        expect(statusOf(fixture, 2).classList).toContain('badge--inactive');
        expect(card(fixture, 2).textContent).toContain('Staff explained: Rome was the capital then');
      });

      it('shows each question’s own dispute', async () => {
        const fixture = await open(
          review({
            disputeWindow: windowOf(),
            disputes: [dispute({ id: 'd1', questionId: 'q1', reason: 'First reason' }), dispute({ id: 'd3', questionId: 'q3', reason: 'Third reason', status: 'Rejected', resolutionNote: 'No' })],
          }),
        );

        expect(card(fixture, 1).textContent).toContain('First reason');
        expect(card(fixture, 2).textContent).not.toContain('Your dispute');
        expect(card(fixture, 3).textContent).toContain('Third reason');
        expect(statusOf(fixture, 1).textContent?.trim()).toBe('Waiting for staff');
        expect(statusOf(fixture, 3).textContent?.trim()).toBe('Rejected — the answer key stands');
      });

      it('shows what the candidate and staff wrote as text, never as markup', async () => {
        const fixture = await open(
          review({ disputeWindow: windowOf(), disputes: [dispute({ reason: '<img src="x" onerror="alert(1)"><b>bold</b>', status: 'Rejected', resolutionNote: '<script>alert(2)</script>' })] }),
        );

        expect(card(fixture, 2).querySelector('img, b, script')).toBeNull();
        expect(card(fixture, 2).textContent).toContain('<b>bold</b>');
        expect(card(fixture, 2).textContent).toContain('<script>alert(2)</script>');
      });
    });

    describe('in the language the candidate chose', () => {
      afterEach(() => localStorage.clear());

      it('is in Hindi, down to the status, the notes and the sentence about the time allowed', async () => {
        localStorage.clear();
        const fixture = await open(
          review({
            resultVersion: 2,
            revisions: [{ previousScore: 2.75, previousMaxScore: 12, newScore: 4, newMaxScore: 12, reason: 'कारण', revisedAtUtc: '2026-10-06T10:00:00Z', version: 2 }],
            disputeWindow: windowOf(),
            disputes: [dispute({ questionId: 'q1', status: 'Accepted', resolutionNote: 'कुंजी में गलती थी' })],
          }),
        );

        TestBed.inject(I18nService).setLanguage('hi');
        fixture.detectChanges();

        const text = textOf(fixture);
        expect(text).toContain('परिणाम का संस्करण 2');
        expect(text).toContain('संस्करण 2:');
        expect(text).toContain(`आप ${formatDate(CLOSES, 'medium', 'en-US')} तक उत्तर कुंजी पर आपत्ति दर्ज कर सकते हैं।`);
        expect(card(fixture, 1).textContent).toContain('आपकी आपत्ति');
        expect(card(fixture, 1).textContent).toContain('स्वीकार की गई — उत्तर कुंजी सुधार दी गई');
        expect(card(fixture, 1).textContent).toContain('सुधार का कारण: कुंजी में गलती थी');
        expect(card(fixture, 2).textContent).toContain('इस उत्तर कुंजी पर आपत्ति दर्ज करें');
      });

      it('is in Marathi, down to the form', async () => {
        localStorage.clear();
        const fixture = await open(review({ disputeWindow: windowOf() }));

        TestBed.inject(I18nService).setLanguage('mr');
        fixture.detectChanges();
        buttonIn(card(fixture, 2), 'या उत्तरतालिकेवर आक्षेप नोंदवा').click();
        fixture.detectChanges();

        const form = card(fixture, 2).querySelector('form') as HTMLFormElement;
        expect(form.getAttribute('aria-label')).toBe('प्रश्न 2 च्या उत्तरतालिकेवर आक्षेप');
        expect(form.textContent).toContain('उत्तरतालिका चुकीची आहे असे तुम्हाला का वाटते?');
        expect(form.textContent).toContain('जास्तीत जास्त 1000 अक्षरे.');
        expect(buttonIn(form, 'आक्षेप पाठवा')).toBeDefined();
        expect(buttonIn(form, 'रद्द करा')).toBeDefined();
      });
    });
  });
});
