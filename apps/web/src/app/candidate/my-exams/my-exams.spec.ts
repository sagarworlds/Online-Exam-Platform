import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
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
    expect(root.querySelector('button')).toBeNull();
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
});
