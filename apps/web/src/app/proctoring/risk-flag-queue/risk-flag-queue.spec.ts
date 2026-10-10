import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { RiskFlag, RiskFlagQueue } from '../proctoring.models';
import { RiskFlagQueue as RiskFlagQueuePage } from './risk-flag-queue';

const flag = (id: string, overrides: Partial<RiskFlag> = {}): RiskFlag => ({
  id,
  attemptId: `a-${id}`,
  candidateId: `c-${id}`,
  candidateEmail: `${id}@example.com`,
  attemptNumber: 1,
  score: 30,
  maxScore: 100,
  status: 'Open',
  computedAtUtc: '2026-10-10T05:00:00Z',
  decidedAtUtc: null,
  decisionNote: null,
  signals: [
    { kind: 'FocusDepartures', value: 3, threshold: 3, raisedWhen: 'AtLeast', weight: 30, raised: true, points: 30 },
    { kind: 'ClientChanges', value: 0, threshold: 2, raisedWhen: 'AtLeast', weight: 10, raised: false, points: 0 },
    { kind: 'Invalidated', value: 0, threshold: 1, raisedWhen: 'AtLeast', weight: 15, raised: false, points: 0 },
    { kind: 'FastCompletion', value: null, threshold: 5, raisedWhen: 'AtMost', weight: 10, raised: false, points: 0 },
    { kind: 'SharedWrongAnswers', value: 0, threshold: 3, raisedWhen: 'AtLeast', weight: 35, raised: false, points: 0 },
  ],
  ...overrides,
});

const queue = (items: RiskFlag[], total = items.length): RiskFlagQueue => ({
  examId: 'e1',
  examName: 'Maths Final',
  filter: 'open',
  page: 1,
  pageSize: 50,
  total,
  items,
});

describe('RiskFlagQueue', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<RiskFlagQueuePage>;
  let root: HTMLElement;
  const base = `${environment.apiBaseUrl}/v1/proctoring`;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RiskFlagQueuePage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'e1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  function open(response: RiskFlagQueue | 'error' = queue([])) {
    fixture = TestBed.createComponent(RiskFlagQueuePage);
    fixture.detectChanges();
    const list = httpMock.expectOne((r) => r.method === 'GET' && r.url === `${base}/exams/e1/risk-flags`);
    expect(list.request.params.get('filter')).toBe('open');
    if (response === 'error') {
      list.flush({ title: 'Server error', detail: 'The queue is not available.' }, { status: 500, statusText: 'Server Error' });
    } else {
      list.flush(response);
    }
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ').trim();
  const buttons = (label: string) =>
    Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === label);
  const press = (label: string) => {
    buttons(label)[0].click();
    fixture.detectChanges();
  };
  const typeNote = (value: string) => {
    const box = root.querySelector<HTMLTextAreaElement>('textarea')!;
    box.value = value;
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  it('shows that the queue is loading until it arrives', () => {
    fixture = TestBed.createComponent(RiskFlagQueuePage);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;

    expect(text()).toContain('Loading');
    httpMock.expectOne((r) => r.url === `${base}/exams/e1/risk-flags`).flush(queue([]));
  });

  it('says so when nothing waits for review, and points to scoring', () => {
    open(queue([]));

    expect(text()).toContain('No flags are waiting for review.');
    expect(text()).toContain('Score the finished attempts to look for flags.');
  });

  it('shows the error with a way to try again, and reads the queue again when asked', () => {
    open('error');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('The queue is not available.');
    press('Try again');
    httpMock.expectOne((r) => r.url === `${base}/exams/e1/risk-flags`).flush(queue([]));
    fixture.detectChanges();
    expect(text()).toContain('No flags are waiting for review.');
  });

  it('shows every signal behind a score, with its value, rule and points in words', () => {
    open(queue([flag('f1')]));

    expect(text()).toContain('Left the exam page');
    expect(text()).toContain('Value: 3 · Raised at 3 or more');
    expect(text()).toContain('Raised · 30 of 30 points');
    expect(text()).toContain('Not raised · 0 of 10 points');
    expect(text()).toContain('Not judged: too few answers to judge the pace');
    expect(text()).toContain('Score 30 of 100');
  });

  it('will not dismiss without a note, and says why on the page', () => {
    open(queue([flag('f1')]));
    press('Dismiss…');

    expect(text()).toContain('Dismissing needs a note saying why.');
    press('Dismiss flag');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Write a note before dismissing.');
    httpMock.expectNone((r) => r.method === 'POST');
  });

  it('dismisses with its note, and the card becomes the line that says so', () => {
    open(queue([flag('f1')]));
    press('Dismiss…');
    typeNote('  Power cut at the centre  ');
    press('Dismiss flag');

    const req = httpMock.expectOne(`${base}/risk-flags/f1/dismiss`);
    expect(req.request.body).toEqual({ note: 'Power cut at the centre' });
    req.flush(null);
    fixture.detectChanges();

    expect(text()).toContain('Dismissed. The note is kept with the decision.');
    expect(buttons('Mark reviewed')).toHaveLength(0);
  });

  it('marks a flag reviewed without a note', () => {
    open(queue([flag('f1')]));
    press('Mark reviewed');

    const req = httpMock.expectOne(`${base}/risk-flags/f1/review`);
    expect(req.request.body).toEqual({ note: null });
    req.flush(null);
    fixture.detectChanges();

    expect(text()).toContain('Marked reviewed. The candidate’s result is unchanged.');
  });

  it('scores the exam when asked, and shows what the scan did', () => {
    open(queue([]));
    press('Score finished attempts');

    httpMock.expectOne(`${base}/exams/e1/risk-scan`).flush({ examId: 'e1', scored: 4, flagged: 1, keptDecided: 0 });
    httpMock.expectOne((r) => r.method === 'GET' && r.url === `${base}/exams/e1/risk-flags`).flush(queue([]));
    fixture.detectChanges();

    expect(text()).toContain('Scored: 4. Flagged: 1. Left as decided: 0.');
  });

  it('shows a flag decided earlier with what was decided, and no buttons', () => {
    open(queue([flag('f1', { status: 'Dismissed', decidedAtUtc: '2026-10-10T06:00:00Z', decisionNote: 'Checked' })]));

    expect(text()).toContain('Dismissed on');
    expect(text()).toContain('Note: Checked');
    expect(buttons('Mark reviewed')).toHaveLength(0);
  });
});
