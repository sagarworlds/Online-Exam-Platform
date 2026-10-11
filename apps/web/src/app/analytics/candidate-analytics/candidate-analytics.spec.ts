import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CandidateAnalyticsDto } from '../analytics.models';
import { CandidateAnalytics } from './candidate-analytics';

describe('CandidateAnalytics', () => {
  let httpMock: HttpTestingController;

  const analytics = (overrides: Partial<CandidateAnalyticsDto> = {}): CandidateAnalyticsDto => ({
    resultCount: 2,
    trend: [
      {
        attemptId: 'a1',
        examId: 'e1',
        examName: 'Maths Mock 1',
        submittedAtUtc: '2026-09-01T10:00:00Z',
        score: 20,
        maxScore: 80,
        percentOfMarks: 25,
      },
      {
        attemptId: 'a2',
        examId: 'e2',
        examName: 'Maths Mock 2',
        submittedAtUtc: '2026-09-08T10:00:00Z',
        score: 60,
        maxScore: 80,
        percentOfMarks: 75,
      },
    ],
    sections: [
      { name: 'Algebra', resultCount: 2, correctCount: 1, wrongCount: 1, partialCount: 0, unansweredCount: 2, accuracy: 50 },
      { name: 'Geometry', resultCount: 1, correctCount: 0, wrongCount: 0, partialCount: 0, unansweredCount: 3, accuracy: null },
    ],
    topics: [
      { name: 'algebra', questionCount: 3, correctCount: 1, wrongCount: 1, partialCount: 0, unansweredCount: 1, accuracy: 50 },
      { name: 'graphs', questionCount: 1, correctCount: 0, wrongCount: 0, partialCount: 0, unansweredCount: 1, accuracy: null },
    ],
    time: {
      questionsTimed: 4,
      averageSeconds: 37.5,
      sections: [
        { name: 'Algebra', questionsTimed: 3, averageSeconds: 40 },
        { name: 'Geometry', questionsTimed: 1, averageSeconds: 25 },
      ],
    },
    ...overrides,
  });

  const root = (fixture: ComponentFixture<CandidateAnalytics>) => fixture.nativeElement as HTMLElement;
  const textOf = (fixture: ComponentFixture<CandidateAnalytics>) => root(fixture).textContent ?? '';

  async function start(): Promise<{ fixture: ComponentFixture<CandidateAnalytics>; request: TestRequest }> {
    await TestBed.configureTestingModule({
      imports: [CandidateAnalytics],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

    const fixture = TestBed.createComponent(CandidateAnalytics);
    fixture.detectChanges();
    const request = httpMock.expectOne((r) => r.url.endsWith('/v1/me/analytics') && r.method === 'GET');
    return { fixture, request };
  }

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('shows a loading notice while the figures are read', async () => {
    const { fixture, request } = await start();

    expect(root(fixture).querySelector('[role="status"]')?.textContent).toContain('Loading');
    expect(root(fixture).querySelector('.analytics-headline')).toBeNull();
    request.flush(analytics());
  });

  it('shows the headline, the trend, the results in order and the sections when there are results', async () => {
    const { fixture, request } = await start();

    request.flush(analytics());
    fixture.detectChanges();

    expect(root(fixture).querySelector('.analytics-headline')?.textContent).toContain('75%');
    expect(root(fixture).querySelectorAll('.trend__marker')).toHaveLength(2);
    expect(root(fixture).querySelectorAll('.analytics-result')).toHaveLength(2);
    const sections = [...root(fixture).querySelectorAll('.analytics-section h3')].map((h) => h.textContent?.trim());
    expect(sections).toEqual(['Algebra', 'Geometry']);
    expect(textOf(fixture)).toContain('50%');
    expect(textOf(fixture)).toContain('No question answered');
  });

  it('shows the accuracy by topic, weakest first, and the time per question with its average and by section', async () => {
    const { fixture, request } = await start();

    request.flush(analytics());
    fixture.detectChanges();

    const topics = [...root(fixture).querySelectorAll('.analytics-topic h3')].map((h) => h.textContent?.trim());
    expect(topics).toEqual(['algebra', 'graphs']);
    expect(textOf(fixture)).toContain('3 questions');
    expect(root(fixture).querySelector('.analytics-time__average strong')?.textContent?.trim()).toBe('37.5 seconds');
    expect(textOf(fixture)).toContain('Across 4 questions with a reported time');
    expect(root(fixture).querySelectorAll('.analytics-time__sections li')).toHaveLength(2);
  });

  it('says no time was reported, rather than showing a zero average, when no question was timed', async () => {
    const { fixture, request } = await start();

    request.flush(analytics({ topics: [], time: { questionsTimed: 0, averageSeconds: null, sections: [] } }));
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('No question with a topic was scored in your released results.');
    expect(textOf(fixture)).toContain('No time on a question was reported in your released results.');
    expect(root(fixture).querySelector('.analytics-time__average')).toBeNull();
  });

  it('marks the chart as decorative, so the results list is what assistive technology reads', async () => {
    const { fixture, request } = await start();

    request.flush(analytics());
    fixture.detectChanges();

    expect(root(fixture).querySelector('svg.trend__svg')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('says so, rather than showing an empty chart, when no result is released yet', async () => {
    const { fixture, request } = await start();

    request.flush(analytics({ resultCount: 0, trend: [], sections: [] }));
    fixture.detectChanges();

    expect(root(fixture).querySelector('.analytics-headline')).toBeNull();
    expect(textOf(fixture)).toContain('Your performance will appear here');
  });

  it('shows a problem and offers to try again, which reads the figures once more', async () => {
    const { fixture, request } = await start();

    request.flush({ title: 'Server error', detail: 'The database is busy.' }, { status: 500, statusText: 'Error' });
    fixture.detectChanges();

    expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('The database is busy.');

    const button = root(fixture).querySelector<HTMLButtonElement>('button.btn');
    button?.click();
    const retry = httpMock.expectOne((r) => r.url.endsWith('/v1/me/analytics'));
    retry.flush(analytics());
    fixture.detectChanges();

    expect(root(fixture).querySelector('.analytics-headline')).not.toBeNull();
  });

  it('opens a point\'s tooltip while the pointer is over it, and closes it when the pointer leaves the chart', async () => {
    const { fixture, request } = await start();
    request.flush(analytics());
    fixture.detectChanges();

    root(fixture).querySelectorAll<SVGGElement>('.trend__point')[1].dispatchEvent(new Event('mouseenter'));
    fixture.detectChanges();
    expect(root(fixture).querySelector('.trend__tip')?.textContent).toContain('Maths Mock 2');

    root(fixture).querySelector<HTMLElement>('.trend__plot')?.dispatchEvent(new Event('mouseleave'));
    fixture.detectChanges();
    expect(root(fixture).querySelector('.trend__tip')).toBeNull();
  });

  it('shows "No marks" for a result whose exam had no marks to earn, rather than a percentage', async () => {
    const { fixture, request } = await start();

    request.flush(
      analytics({
        resultCount: 1,
        trend: [{ attemptId: 'a1', examId: 'e1', examName: 'Unmarked', submittedAtUtc: '2026-09-01T10:00:00Z', score: 0, maxScore: 0, percentOfMarks: null }],
        sections: [],
      }),
    );
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('No marks');
  });
});
