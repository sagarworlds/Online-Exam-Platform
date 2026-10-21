import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { provideRouter } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { AttemptSummaryDto } from '../candidate.models';
import { MyExams } from './my-exams';

describe('MyExams', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    // The choice to show or hide the attempted exams is remembered on the device, so a test must not inherit the last one's.
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [MyExams],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

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
    canRequestAttempt: false,
    attemptRequest: null,
    rules: null,
    ...overrides,
  });

  const request = (overrides: Record<string, unknown> = {}) => ({
    id: 'r1', status: 'Pending', message: null, requestedAtUtc: '2026-10-05T05:00:00Z', decidedAtUtc: null, decisionNote: null, ...overrides,
  });

  const isMyExams = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/me/exams');

  function openWith(exams: ReturnType<typeof exam>[]) {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    httpMock.expectOne(isMyExams).flush(exams);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  const buttonIn = (root: HTMLElement, label: string) =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement | undefined;

  /** The button that shows or hides the attempted exams, whatever its words, or undefined when the page has none. */
  const attemptedToggle = (root: HTMLElement) => root.querySelector<HTMLButtonElement>('.exam-section__toggle') ?? undefined;

  function toggleAttempted(root: HTMLElement, fixture: { detectChanges(): void }) {
    attemptedToggle(root)?.click();
    fixture.detectChanges();
  }

  /** The titles of the exams on the page, in the order they are shown. */
  const titlesIn = (root: HTMLElement) => Array.from(root.querySelectorAll('article .card__title')).map((title) => title.childNodes[0].textContent?.trim());

  it('lists the exams with their state, size and duration', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush([
      exam({}),
      exam({ examId: 'e2', name: 'Physics', state: 'NotOpen', durationSeconds: null, questionCount: 5, canStartAttempt: false }),
      exam({ examId: 'e3', name: 'Chemistry', state: 'Closed', canStartAttempt: false }),
    ]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    // The closed exam has nothing to do with it, so it waits under the folded heading until the candidate opens it.
    expect(root.textContent).not.toContain('Chemistry');
    toggleAttempted(root, fixture);
    const text = root.textContent ?? '';
    expect(text).toContain('Maths Final');
    expect(text).toContain('Not open yet');
    expect(text).toContain('Closed');
    // Each fact is its own item, so a phone can wrap them without splitting one sentence across lines.
    const facts = Array.from(root.querySelectorAll('.exam-facts')).map((list) =>
      Array.from(list.querySelectorAll('li')).map((item) => item.textContent?.trim()),
    );
    expect(facts).toEqual([['20 questions', '90 minutes'], ['5 questions'], ['20 questions', '90 minutes']]);
  });

  it('is shown in the language the candidate chose, and changes with it', async () => {
    localStorage.clear();
    const { fixture, root } = openWith([exam({ attemptsAllowed: 3, attemptsUsed: 1, attempts: [attempt(1, 'Submitted', 15)] })]);
    const i18n = TestBed.inject(I18nService);

    await i18n.setLanguage('hi');
    fixture.detectChanges();

    const text = root.textContent ?? '';
    expect(text).toContain('मेरी परीक्षाएँ');
    expect(text).toContain('20 प्रश्न');
    expect(text).toContain('90 मिनट');
    expect(text).toContain('उपयोग किए गए प्रयास: 1 में से 3');
    expect(text).toContain('आपका स्कोर:');
    expect(root.textContent).toContain('प्रयास 2 शुरू करें');
    expect(root.textContent).not.toContain('My exams');

    await i18n.setLanguage('mr');
    fixture.detectChanges();
    expect(root.textContent).toContain('माझ्या परीक्षा');
    expect(root.textContent).toContain('प्रयत्न 2 सुरू करा');
    localStorage.clear();
  });

  it('says how much time the candidate\'s accommodation adds, and nothing when there is none (FR-49)', () => {
    const { root } = openWith([
      exam({ accommodation: { extraTimeSeconds: 1800, readerScribe: true, alternateFormats: [] } }),
      exam({ examId: 'e2', name: 'Physics' }),
    ]);

    const cards = Array.from(root.querySelectorAll('.card'));
    expect(cards[0].textContent).toContain('Your accommodation adds 30 minutes to the time allowed.');
    expect(cards[1].textContent).not.toContain('accommodation');
  });

  it('says nothing about time for an accommodation that gives none, such as a reader alone', () => {
    const { root } = openWith([exam({ accommodation: { extraTimeSeconds: 0, readerScribe: true, alternateFormats: [] } })]);

    expect(root.textContent).not.toContain('Your accommodation adds');
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

    const starts = (fixture.nativeElement as HTMLElement).querySelectorAll('a.btn--primary');
    expect(starts.length).toBe(1);
    expect(starts[0].textContent).toContain('Start exam');
  });

  it('sends the candidate to the instructions page, not straight into the exam, when Start is pressed', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush([exam({})]);
    fixture.detectChanges();

    const start = (fixture.nativeElement as HTMLElement).querySelector('a.btn--primary') as HTMLAnchorElement;

    expect(start.getAttribute('href')).toBe('/my-exams/e1/start');
    // Nothing is started from this page: the attempt begins on the instructions page, after the acknowledgment.
    httpMock.expectNone((r) => r.url.endsWith('/attempts'));
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
    expect(root.querySelector('article button')).toBeNull();
    const link = Array.from(root.querySelectorAll('a')).find((a) => a.textContent?.includes('View result'));
    expect(link?.getAttribute('href')).toBe('/attempt/a1');
  });
  it('shows the one action above the attempt history, so the candidate reaches it first', () => {
    const { root } = openWith([exam({ attemptsUsed: 1, canStartAttempt: true, attempts: [attempt(1, 'Submitted', 11)], attemptsAllowed: 2 })]);

    const action = root.querySelector('.actions a.btn--primary') as HTMLElement;
    const history = root.querySelector('.attempt-list') as HTMLElement;
    expect(action.textContent).toContain('Start attempt 2');
    expect(action.compareDocumentPosition(history) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('announces that the exams are loading, and a failure to load them as an alert', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Loading');

    httpMock
      .expectOne(isMyExams)
      .flush({ detail: 'The exams could not be loaded. Try again later.' }, { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('The exams could not be loaded.');
  });

  it('shows grey placeholder cards while the exams are read, and says it is loading', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelectorAll('.skeleton')).toHaveLength(2);
    expect(root.querySelector('.skeleton')?.getAttribute('aria-hidden')).toBe('true');
    expect(root.querySelector('[role="status"] .visually-hidden')?.textContent).toContain('Loading');

    httpMock.expectOne(isMyExams).flush([]);
    fixture.detectChanges();
    expect(root.querySelector('.skeleton')).toBeNull();
  });

  it('offers Try again when the exams cannot be read, without saying there are none, and shows them once they can', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    httpMock.expectOne(isMyExams).flush({ detail: 'Down for a moment.' }, { status: 503, statusText: 'Service Unavailable' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Down for a moment.');
    // Neither "no exams yet" nor "nothing to take" may sit beside the failure: the list was not read, so nothing is known about it.
    expect(root.textContent).not.toContain('You have no exams yet');
    expect(root.textContent).not.toContain('Nothing to take right now.');
    expect(root.querySelector('.exam-section')).toBeNull();
    buttonIn(root, 'Try again')!.click();
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(root.querySelectorAll('.skeleton').length).toBeGreaterThan(0);

    httpMock.expectOne(isMyExams).flush([exam({})]);
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(titlesIn(root)).toEqual(['Maths Final']);
  });

  it('keeps the performance link at the end of the page, not beside the title where the exams are looked for', () => {
    const { root } = openWith([exam({})]);

    const link = root.querySelector<HTMLAnchorElement>('a[href="/analytics"]')!;
    expect(root.querySelector('.page-header a')).toBeNull();
    expect(root.querySelector('article')!.compareDocumentPosition(link) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  describe('how long the candidate has', () => {
    beforeEach(() => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-10-05T05:00:00Z'));
    });
    afterEach(() => vi.useRealTimers());

    const countdownOf = (root: HTMLElement) => root.querySelector('.exam-card__countdown')?.textContent?.trim();

    it('says when an open exam closes, in its two largest units', () => {
      // 07:30 is 2 hours 30 minutes from 05:00.
      expect(countdownOf(openWith([exam({})]).root)).toBe('Closes in 2 hours 30 minutes');
    });

    it('says when an exam that has not opened yet opens, in days and hours', () => {
      const { root } = openWith([exam({ state: 'NotOpen', canStartAttempt: false, startUtc: '2026-10-07T08:05:00Z', endUtc: '2026-10-07T11:00:00Z' })]);

      expect(countdownOf(root)).toBe('Opens in 2 days 3 hours');
    });

    it('says minutes alone when under an hour is left, and "less than a minute" at the end', () => {
      expect(countdownOf(openWith([exam({ endUtc: '2026-10-05T05:12:30Z' })]).root)).toBe('Closes in 12 minutes');
    });

    it('moves on with the clock while the page stays open', () => {
      const { fixture, root } = openWith([exam({ endUtc: '2026-10-05T05:01:20Z' })]);
      expect(countdownOf(root)).toBe('Closes in 1 minute');

      vi.advanceTimersByTime(30_000);
      fixture.detectChanges();
      expect(countdownOf(root)).toBe('Closes in less than a minute');
    });

    it('shows no countdown once the moment has passed on this device, instead of "closes in less than a minute" for ever', () => {
      const { root } = openWith([exam({ endUtc: '2026-10-05T04:59:00Z' })]);

      expect(countdownOf(root)).toBeUndefined();
    });

    it('shows no countdown for an exam that is over', () => {
      const { root, fixture } = openWith([exam({ state: 'Closed', canStartAttempt: false, attempts: [attempt(1, 'Submitted', 10)] })]);
      toggleAttempted(root, fixture);

      expect(root.querySelector('.exam-card__countdown')).toBeNull();
    });
  });

  describe('the exam to do next', () => {
    it('is the first of those available now, and only that one', () => {
      const { root } = openWith([
        exam({ examId: 'e1', name: 'Soon', endUtc: '2026-10-05T06:00:00Z' }),
        exam({ examId: 'e2', name: 'Later', endUtc: '2026-10-06T06:00:00Z' }),
        exam({ examId: 'e3', name: 'Not yet', state: 'NotOpen', canStartAttempt: false }),
      ]);

      const next = Array.from(root.querySelectorAll('article')).filter((card) => card.classList.contains('exam-card--next'));
      expect(next).toHaveLength(1);
      expect(next[0].querySelector('.card__title')?.childNodes[0].textContent?.trim()).toBe('Soon');
    });
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

      const [multi, single] = Array.from(root.querySelectorAll('article.card'));
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

      const starts = root.querySelectorAll('a.btn--primary');
      expect(starts.length).toBe(1);
      expect(starts[0].textContent).toContain('Start attempt 2');
    });

    it('offers a resume, not a new attempt, while one is open', () => {
      const { root } = open([
        twoAttempts({ attemptStatus: 'InProgress', score: null, attemptsUsed: 2, canStartAttempt: false, attempts: [attempt(1, 'Submitted', 11), attempt(2, 'InProgress')] }),
      ]);

      expect(root.querySelector('a[href$="/start"]')).toBeNull();
      // The open attempt is listed without a button of its own; the one action above the list carries it.
      expect(rows(root)[1].textContent).toContain('In progress');
      expect(rows(root)[1].querySelector('a')).toBeNull();
      const resume = root.querySelector('.actions a.btn--primary');
      expect(resume?.textContent).toContain('Resume exam');
      expect(resume?.getAttribute('href')).toBe('/attempt/a2');
    });

    it('sends the candidate through the instructions page to start the next attempt', () => {
      const { root } = open([twoAttempts({ attemptsUsed: 1, canStartAttempt: true, attempts: [attempt(1, 'Submitted', 11)] })]);

      expect(root.querySelector('a[href$="/start"]')?.getAttribute('href')).toBe('/my-exams/e1/start');
    });
  });

  describe('asking for another attempt', () => {
    const used = { canStartAttempt: false, canRequestAttempt: true, attemptsUsed: 1, attempts: [attempt(1, 'Submitted', 4)] };

    it('offers the request only when the API says one may be made', () => {
      const { root } = openWith([exam({ ...used }), exam({ examId: 'e2', name: 'Physics', canStartAttempt: false })]);

      expect(root.querySelectorAll('.card').length).toBe(2);
      expect(Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === 'Ask for another attempt').length).toBe(1);
    });

    it('sends the message, then reads the exams again to show the request waiting', () => {
      const { fixture, root } = openWith([exam({ ...used })]);
      buttonIn(root, 'Ask for another attempt')!.click();
      fixture.detectChanges();
      const box = root.querySelector('textarea') as HTMLTextAreaElement;
      // The text box is named by its question, so a screen reader announces what it is for.
      expect(box.labels?.[0]?.textContent).toContain('Why do you need another attempt?');
      box.value = '  Power cut  ';
      box.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      buttonIn(root, 'Send request')!.click();

      const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/me/exams/e1/attempt-requests'));
      expect(post.request.body).toEqual({ message: 'Power cut' });
      post.flush(request({ message: 'Power cut' }), { status: 201, statusText: 'Created' });
      httpMock.expectOne(isMyExams).flush([exam({ ...used, canRequestAttempt: false, attemptRequest: request({ message: 'Power cut' }) })]);
      fixture.detectChanges();

      expect(root.textContent).toContain('waiting for an administrator');
      expect(buttonIn(root, 'Ask for another attempt')).toBeUndefined();
    });

    it('sends no message when none was typed', () => {
      const { fixture, root } = openWith([exam({ ...used })]);
      buttonIn(root, 'Ask for another attempt')!.click();
      fixture.detectChanges();

      buttonIn(root, 'Send request')!.click();

      const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/attempt-requests'));
      expect(post.request.body).toEqual({ message: null });
      post.flush(request(), { status: 201, statusText: 'Created' });
      httpMock.expectOne(isMyExams).flush([exam({ ...used })]);
    });

    it('shows the API’s reason and keeps the form open when the request is refused', () => {
      const { fixture, root } = openWith([exam({ ...used })]);
      buttonIn(root, 'Ask for another attempt')!.click();
      fixture.detectChanges();

      buttonIn(root, 'Send request')!.click();
      httpMock
        .expectOne((r) => r.method === 'POST' && r.url.endsWith('/attempt-requests'))
        .flush({ title: 'attempt_request_pending', detail: 'You already have a request for this exam waiting for an administrator.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(root.querySelector('[role="alert"]')?.textContent).toContain('already have a request');
      expect(root.querySelector('textarea')).not.toBeNull();
    });

    it('closes the form again on Cancel without sending anything', () => {
      const { fixture, root } = openWith([exam({ ...used })]);
      buttonIn(root, 'Ask for another attempt')!.click();
      fixture.detectChanges();

      buttonIn(root, 'Cancel')!.click();
      fixture.detectChanges();

      expect(root.querySelector('textarea')).toBeNull();
      httpMock.expectNone((r) => r.method === 'POST');
    });

    it('moves focus into the request form when it opens, and back to its button when it is cancelled', async () => {
      const { fixture, root } = openWith([exam({ ...used })]);
      buttonIn(root, 'Ask for another attempt')!.click();
      fixture.detectChanges();
      await fixture.whenStable();
      expect(document.activeElement).toBe(root.querySelector('textarea'));

      buttonIn(root, 'Cancel')!.click();
      fixture.detectChanges();
      await fixture.whenStable();
      expect(document.activeElement).toBe(buttonIn(root, 'Ask for another attempt'));
    });

    it('moves focus to the exam’s title once the request is sent, since the button that asked is gone', async () => {
      const { fixture, root } = openWith([exam({ ...used })]);
      buttonIn(root, 'Ask for another attempt')!.click();
      fixture.detectChanges();
      buttonIn(root, 'Send request')!.click();
      httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/attempt-requests')).flush(request(), { status: 201, statusText: 'Created' });
      httpMock.expectOne(isMyExams).flush([exam({ ...used, canRequestAttempt: false, attemptRequest: request() })]);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(root.querySelector('#exam-title-e1'));
    });

    it('puts focus back on Send when the request is refused, since the button was disabled while it ran', async () => {
      const { fixture, root } = openWith([exam({ ...used })]);
      buttonIn(root, 'Ask for another attempt')!.click();
      fixture.detectChanges();
      buttonIn(root, 'Send request')!.click();
      httpMock
        .expectOne((r) => r.method === 'POST' && r.url.endsWith('/attempt-requests'))
        .flush({ title: 'attempt_request_pending', detail: 'You already have a request for this exam waiting for an administrator.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(buttonIn(root, 'Send request'));
    });

    it('says a request was declined, with the reason, and lets the candidate ask again', () => {
      const { root } = openWith([exam({ ...used, attemptRequest: request({ status: 'Declined', decisionNote: 'Speak to your teacher' }) })]);

      expect(root.textContent).toContain('was declined: Speak to your teacher');
      expect(buttonIn(root, 'Ask for another attempt')).toBeDefined();
    });

    it('does not mention a declined request once another attempt can be started', () => {
      const { root } = openWith([exam({ attemptsAllowed: 2, attemptsUsed: 1, attempts: [attempt(1, 'Submitted', 4)], attemptRequest: request({ status: 'Approved' }) })]);

      expect(root.textContent).not.toContain('declined');
      expect(root.textContent).toContain('Start attempt 2');
    });
  });

  // A candidate looks for the exam they have not taken yet, so what they can do comes first and what is done is folded away below it.
  describe('what the candidate can take comes first', () => {
    const done = (examId: string, name: string, submittedAt: string) =>
      exam({
        examId, name, state: 'Closed', canStartAttempt: false, attemptsUsed: 1,
        attempts: [{ ...attempt(1, 'Submitted', 15), id: `a-${examId}`, submittedAtUtc: submittedAt }],
      });

    it('puts a new exam above the exams already attempted, whatever order the API lists them in', () => {
      const { fixture, root } = openWith([done('d1', 'Old result', '2026-10-01T05:00:00Z'), exam({ examId: 'n1', name: 'New exam' })]);

      expect(titlesIn(root)).toEqual(['New exam']);
      toggleAttempted(root, fixture);

      expect(titlesIn(root)).toEqual(['New exam', 'Old result']);
    });

    it('folds the attempted exams away, with their number on the heading, while there is something else to see', () => {
      const { root } = openWith([done('d1', 'Old result', '2026-10-01T05:00:00Z'), done('d2', 'Older result', '2026-09-01T05:00:00Z'), exam({ examId: 'n1' })]);

      const toggle = attemptedToggle(root) as HTMLButtonElement;
      expect(toggle.textContent).toContain('Attempted and closed exams (2)');
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(root.textContent).not.toContain('Old result');
    });

    it('shows and hides them again from the same button, which stays below the new exams so they never move', () => {
      const { fixture, root } = openWith([done('d1', 'Old result', '2026-10-01T05:00:00Z'), exam({ examId: 'n1', name: 'New exam' })]);
      const toggle = attemptedToggle(root) as HTMLButtonElement;
      const newCard = root.querySelector('article') as HTMLElement;

      toggle.click();
      fixture.detectChanges();
      expect(toggle.getAttribute('aria-expanded')).toBe('true');
      expect(root.textContent).toContain('Old result');
      // In the page's order: the new exam, then the heading button, then the results.
      expect(newCard.compareDocumentPosition(toggle) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(titlesIn(root)).toEqual(['New exam', 'Old result']);

      toggle.click();
      fixture.detectChanges();
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(root.textContent).not.toContain('Old result');
      expect(titlesIn(root)).toEqual(['New exam']);
    });

    it('shows the attempted exams at once when there is nothing else, so the page is not left looking empty', () => {
      const { root } = openWith([done('d1', 'Old result', '2026-10-01T05:00:00Z')]);

      expect(attemptedToggle(root)?.getAttribute('aria-expanded')).toBe('true');
      expect(titlesIn(root)).toEqual(['Old result']);
    });

    it('says so when there is nothing to take now, rather than leaving the candidate to look for it', () => {
      const { root } = openWith([done('d1', 'Old result', '2026-10-01T05:00:00Z'), exam({ examId: 'f1', name: 'Next week', state: 'NotOpen', canStartAttempt: false })]);

      expect(root.querySelector('#exams-available-heading')?.textContent).toContain('Available now');
      expect(root.textContent).toContain('Nothing to take right now.');
      expect(root.querySelector('#exams-upcoming-heading')?.textContent).toContain('Coming up');
      expect(titlesIn(root)).toEqual(['Next week']);
    });

    it('does not say so when there is something to take', () => {
      const { root } = openWith([exam({})]);

      expect(root.textContent).not.toContain('Nothing to take right now.');
    });

    it('lists an exam in progress first, then the one that closes soonest, then the one that closes last', () => {
      const { root } = openWith([
        exam({ examId: 'late', name: 'Closes last', endUtc: '2026-10-09T07:30:00Z' }),
        exam({ examId: 'soon', name: 'Closes soonest', endUtc: '2026-10-06T07:30:00Z' }),
        exam({ examId: 'busy', name: 'In progress', endUtc: '2026-10-12T07:30:00Z', canStartAttempt: false, attemptsUsed: 1, attempts: [attempt(1, 'InProgress')] }),
      ]);

      expect(titlesIn(root)).toEqual(['In progress', 'Closes soonest', 'Closes last']);
    });

    it('keeps an exam the candidate can ask for another attempt at among the ones to act on', () => {
      const { root } = openWith([
        done('d1', 'Old result', '2026-10-01T05:00:00Z'),
        exam({ examId: 'r1', name: 'Ask again', canStartAttempt: false, canRequestAttempt: true, attemptsUsed: 1, attempts: [attempt(1, 'Submitted', 4)] }),
      ]);

      expect(titlesIn(root)).toEqual(['Ask again']);
    });

    it('remembers whether the candidate left the attempted exams open, for the next visit', () => {
      const first = openWith([done('d1', 'Old result', '2026-10-01T05:00:00Z'), exam({ examId: 'n1' })]);
      toggleAttempted(first.root, first.fixture);
      first.fixture.destroy();

      const again = openWith([done('d1', 'Old result', '2026-10-01T05:00:00Z'), exam({ examId: 'n1' })]);

      expect(attemptedToggle(again.root)?.getAttribute('aria-expanded')).toBe('true');
      expect(titlesIn(again.root)).toContain('Old result');
    });

    it('honours a choice to hide them even when there is nothing else on the page', () => {
      localStorage.setItem('exam-platform.my-exams.attempted-open', '0');

      const { root } = openWith([done('d1', 'Old result', '2026-10-01T05:00:00Z')]);

      expect(attemptedToggle(root)?.getAttribute('aria-expanded')).toBe('false');
      expect(titlesIn(root)).toEqual([]);
    });

    it('says all this in the language the candidate chose', async () => {
      const { fixture, root } = openWith([done('d1', 'Old result', '2026-10-01T05:00:00Z'), exam({ examId: 'n1' })]);

      await TestBed.inject(I18nService).setLanguage('hi');
      fixture.detectChanges();

      expect(root.querySelector('#exams-available-heading')?.textContent).toContain('अभी उपलब्ध');
      expect(attemptedToggle(root)?.textContent).toContain('दी हुई और बंद परीक्षाएँ (1)');
      expect(attemptedToggle(root)?.textContent).toContain('दिखाएँ');
      await TestBed.inject(I18nService).setLanguage('en');
    });
  });
});
