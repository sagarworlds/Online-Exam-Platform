import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { MyExamDto } from '../candidate.models';
import { CheckResult, SystemCheckService } from '../system-check/system-check';
import { ExamInstructions } from './exam-instructions';

const PASSING: CheckResult[] = [
  { id: 'browser', label: 'Browser', status: 'pass', detail: 'Your browser can run the exam.' },
  { id: 'connection', label: 'Internet connection', status: 'pass', detail: 'You are online.' },
  { id: 'server', label: 'Exam server', status: 'pass', detail: 'The exam server answers quickly.' },
  { id: 'bandwidth', label: 'Connection speed', status: 'info', detail: 'Not reported.' },
];

function exam(overrides: Partial<MyExamDto> = {}): MyExamDto {
  return {
    examId: 'e1',
    name: 'Physics Midterm',
    description: 'Chapters 1 to 4',
    startUtc: '2026-10-05T04:30:00Z',
    endUtc: '2026-10-05T07:30:00Z',
    lateEntryDeadlineUtc: null,
    durationSeconds: 3600,
    questionCount: 20,
    state: 'Open',
    attemptId: null,
    attemptStatus: null,
    score: null,
    maxScore: null,
    attemptsAllowed: 1,
    attemptsUsed: 0,
    canStartAttempt: true,
    attempts: [],
    canRequestAttempt: false,
    attemptRequest: null,
    rules: { correctMarks: 4, incorrectMarks: -1, unattemptedMarks: 0, partialCredit: false, sectionLock: false, sectionCount: 1 },
    ...overrides,
  };
}

describe('ExamInstructions', () => {
  const accommodated = (overrides: Partial<NonNullable<MyExamDto['accommodation']>> = {}) =>
    exam({ accommodation: { extraTimeSeconds: 1800, readerScribe: false, alternateFormats: [], ...overrides } });

  let httpMock: HttpTestingController;
  let run: ReturnType<typeof vi.fn<() => Promise<CheckResult[]>>>;

  beforeEach(() => {
    run = vi.fn<() => Promise<CheckResult[]>>().mockResolvedValue(PASSING);
    TestBed.configureTestingModule({
      imports: [ExamInstructions],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ examId: 'e1' }) } } },
        { provide: SystemCheckService, useValue: { run } },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function open(exams: MyExamDto[] = [exam()]): Promise<ComponentFixture<ExamInstructions>> {
    const fixture = TestBed.createComponent(ExamInstructions);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush(exams);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<ExamInstructions>) => fixture.nativeElement as HTMLElement;

  it('shows the extra time an accommodation gives, beside the time allowed, and says so in the instructions (FR-49)', async () => {
    const fixture = await open([accommodated({ readerScribe: true })]);

    const facts = Array.from(root(fixture).querySelectorAll('.facts div')).map(
      (d) => `${d.querySelector('dt')?.textContent?.trim()} ${d.querySelector('dd')?.textContent?.trim()}`,
    );
    expect(facts).toContain('Time allowed 60 minutes');
    expect(facts).toContain('Extra time (your accommodation) 30 minutes');
    const rules = root(fixture).querySelector('.rules')?.textContent ?? '';
    expect(rules).toContain('30 minutes of extra time');
    expect(rules).toContain('You may use a reader or scribe');
  });

  it('shows no extra-time row, and no accommodation sentences, for a candidate who has none', async () => {
    const fixture = await open();

    expect(root(fixture).textContent).not.toContain('Extra time');
    expect(root(fixture).querySelector('.rules')?.textContent).not.toContain('accommodation');
  });

  it('tells a screen reader user that leaving the page is not counted against them', async () => {
    const fixture = await open([accommodated({ extraTimeSeconds: 0, alternateFormats: ['screen_reader'] })]);

    expect(root(fixture).textContent).toContain('leaving the exam page is not counted against you');
    expect(root(fixture).textContent).not.toContain('Extra time');
  });

  const startButton = (fixture: ComponentFixture<ExamInstructions>) =>
    Array.from(root(fixture).querySelectorAll('button')).find((b) => b.textContent?.includes('Start')) as HTMLButtonElement;
  const checkbox = (fixture: ComponentFixture<ExamInstructions>) => root(fixture).querySelector('input[type="checkbox"]') as HTMLInputElement;

  async function tick(fixture: ComponentFixture<ExamInstructions>, action: () => void): Promise<void> {
    action();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('shows the exam, its instructions drawn from its own rules, and the system check', async () => {
    const fixture = await open();
    const text = root(fixture).textContent ?? '';

    expect(text).toContain('Physics Midterm');
    expect(text).toContain('60 minutes');
    expect(text).toContain('Each correct answer earns 4 marks.');
    expect(text).toContain('A wrong answer costs 1 mark.');
    expect(text).toContain('Exam server');
    expect(text).toContain('All checks passed.');
  });

  it('keeps Start disabled until the instructions are acknowledged', async () => {
    const fixture = await open();
    expect(startButton(fixture).disabled).toBe(true);

    await tick(fixture, () => checkbox(fixture).click());

    expect(startButton(fixture).disabled).toBe(false);
  });

  it('keeps Start disabled while the system check is still running', async () => {
    let finish!: (results: CheckResult[]) => void;
    run.mockReturnValue(new Promise<CheckResult[]>((resolve) => (finish = resolve)));
    const fixture = await open();
    await tick(fixture, () => checkbox(fixture).click());

    expect(root(fixture).textContent).toContain('Checking your browser and connection');
    expect(startButton(fixture).disabled).toBe(true);

    finish(PASSING);
    await tick(fixture, () => undefined);
    expect(startButton(fixture).disabled).toBe(false);
  });

  it('blocks Start when the check finds a problem, even after the acknowledgment, and says what to do', async () => {
    run.mockResolvedValue([{ id: 'server', label: 'Exam server', status: 'fail', detail: 'The exam server could not be reached.' }]);
    const fixture = await open();

    await tick(fixture, () => checkbox(fixture).click());

    expect(startButton(fixture).disabled).toBe(true);
    expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('You cannot start until it passes');
    expect(root(fixture).textContent).toContain('The exam server could not be reached.');
  });

  it('lets the candidate go on past a warning, saying the exam may only feel slower', async () => {
    run.mockResolvedValue([{ id: 'server', label: 'Exam server', status: 'warn', detail: 'The server answers a little slowly.' }]);
    const fixture = await open();

    await tick(fixture, () => checkbox(fixture).click());

    expect(startButton(fixture).disabled).toBe(false);
    expect(root(fixture).textContent).toContain('you can go on');
  });

  it('runs the check again on request, and a fixed problem no longer blocks', async () => {
    run.mockResolvedValueOnce([{ id: 'connection', label: 'Internet connection', status: 'fail', detail: 'You are offline.' }]);
    const fixture = await open();
    await tick(fixture, () => checkbox(fixture).click());
    expect(startButton(fixture).disabled).toBe(true);

    const again = Array.from(root(fixture).querySelectorAll('button')).find((b) => b.textContent?.includes('Run the check again')) as HTMLButtonElement;
    await tick(fixture, () => again.click());

    expect(run).toHaveBeenCalledTimes(2);
    expect(startButton(fixture).disabled).toBe(false);
  });

  it('starts the attempt with the acknowledgment and opens it', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = await open();
    await tick(fixture, () => checkbox(fixture).click());

    startButton(fixture).click();
    const start = httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams/e1/attempts'));
    expect(start.request.method).toBe('POST');
    expect(start.request.body).toEqual({ instructionsAcknowledged: true });
    start.flush({ id: 'attempt-9' });

    expect(navigate).toHaveBeenCalledWith(['/attempt', 'attempt-9']);
  });

  it('shows why the exam could not be started and lets the candidate try again', async () => {
    const fixture = await open();
    await tick(fixture, () => checkbox(fixture).click());

    startButton(fixture).click();
    httpMock
      .expectOne((r) => r.url.endsWith('/v1/me/exams/e1/attempts'))
      .flush({ title: 'exam_closed', detail: 'This exam is closed; it can no longer be started.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(root(fixture).textContent).toContain('This exam is closed');
    expect(startButton(fixture).disabled).toBe(false);
  });

  it('cannot be double-submitted while the start is in flight', async () => {
    // Navigation is stubbed: this router has no routes, and a real navigation to /attempt/a1 would fail as an unhandled error.
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = await open();
    await tick(fixture, () => checkbox(fixture).click());

    startButton(fixture).click();
    fixture.detectChanges();
    expect(startButton(fixture).disabled).toBe(true);
    startButton(fixture).click();

    // expectOne fails if the second click sent a second request.
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams/e1/attempts')).flush({ id: 'a1' });
    expect(navigate).toHaveBeenCalledTimes(1);
  });

  it('offers no acknowledgment or Start for an exam that has not opened, and says why', async () => {
    const fixture = await open([exam({ state: 'NotOpen', canStartAttempt: false })]);

    expect(root(fixture).textContent).toContain('has not opened yet');
    expect(checkbox(fixture)).toBeNull();
    expect(startButton(fixture)).toBeUndefined();
  });

  it('says the question paper is locked until the exam opens, and when it opens (#57)', async () => {
    const fixture = await open([exam({ state: 'NotOpen', canStartAttempt: false })]);

    const notice = root(fixture).querySelector('.paper-status');
    expect(notice?.classList).not.toContain('paper-status--open');
    expect(notice?.textContent).toContain('The question paper is not available yet');
    expect(notice?.textContent).toContain('It opens with the exam at');
  });

  it('says the question paper is open once the exam has opened, and does not call it locked (#57)', async () => {
    const fixture = await open([exam({ state: 'Open', canStartAttempt: true })]);

    const notice = root(fixture).querySelector('.paper-status');
    expect(notice?.classList).toContain('paper-status--open');
    expect(notice?.textContent).toContain('The question paper is open');
    expect(notice?.textContent).not.toContain('not available yet');
  });

  it('points a candidate with an attempt in progress to resume it', async () => {
    const fixture = await open([exam({ canStartAttempt: false, attemptId: 'a7', attemptStatus: 'InProgress', attemptsUsed: 1 })]);

    expect(root(fixture).textContent).toContain('You already have an attempt in progress.');
    const resume = Array.from(root(fixture).querySelectorAll('a')).find((a) => a.textContent?.includes('Resume exam'));
    expect(resume?.getAttribute('href')).toBe('/attempt/a7');
  });

  it('says so when the exam is not among the candidate\'s', async () => {
    const fixture = await open([exam({ examId: 'someone-else' })]);

    expect(root(fixture).textContent).toContain('We could not find this exam among yours.');
  });

  it('shows the error when the exams cannot be loaded', async () => {
    const fixture = TestBed.createComponent(ExamInstructions);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush({ title: 'x', detail: 'Server unavailable.' }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('Server unavailable.');
  });

  describe('the start bar', () => {
    const reason = (fixture: ComponentFixture<ExamInstructions>) => root(fixture).querySelector('#start-reason')?.textContent?.trim();

    it('says what is still needed beside Start: the check, then a problem, then the box, then ready', async () => {
      let finish!: (results: CheckResult[]) => void;
      run.mockReturnValueOnce(new Promise<CheckResult[]>((resolve) => (finish = resolve)));
      const fixture = await open();
      expect(reason(fixture)).toBe('Checking your browser and connection…');

      finish(PASSING);
      await tick(fixture, () => undefined);
      expect(reason(fixture)).toBe('Tick the box above to start.');

      await tick(fixture, () => checkbox(fixture).click());
      expect(reason(fixture)).toBe('Ready to start.');

      run.mockResolvedValueOnce([{ id: 'server', label: 'Exam server', status: 'fail', detail: 'The exam server could not be reached.' }]);
      await tick(fixture, () => Array.from(root(fixture).querySelectorAll('button')).find((b) => b.textContent?.includes('Run the check again'))?.click());
      expect(reason(fixture)).toBe('Fix the problem in the system check first.');
    });

    it('connects that reason to the Start button, so a screen reader hears why Start is off', async () => {
      const fixture = await open();

      expect(startButton(fixture).getAttribute('aria-describedby')).toBe('start-reason');
      expect(startButton(fixture).disabled).toBe(true);
      expect(reason(fixture)).toBe('Tick the box above to start.');
    });

    it('keeps the time warning in view before the box is ticked, since that is when it matters', async () => {
      const fixture = await open();

      expect(root(fixture).textContent).toContain('The time begins as soon as you press Start exam.');
    });

    it('comes after the rules and the acknowledgment, so Start is the last thing read', async () => {
      const fixture = await open();
      const el = (selector: string) => root(fixture).querySelector(selector) as HTMLElement;
      const follows = (earlier: HTMLElement, later: HTMLElement) => !!(earlier.compareDocumentPosition(later) & Node.DOCUMENT_POSITION_FOLLOWING);

      expect(follows(el('#check-heading'), el('#rules-heading'))).toBe(true);
      expect(follows(el('#rules-heading'), el('#ack-heading'))).toBe(true);
      expect(follows(el('#ack-heading'), el('.start-bar'))).toBe(true);
      expect(el('.start-bar').contains(startButton(fixture))).toBe(true);
    });
  });
});
