import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
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
    vi.restoreAllMocks();
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

  it('shows no time section at all when the time is withheld, rather than a zero', async () => {
    const { fixture, request } = await start();

    request.flush(analytics({ time: null }));
    fixture.detectChanges();

    expect(root(fixture).querySelector('#analytics-time-heading')).toBeNull();
    expect(root(fixture).querySelector('.analytics-time')).toBeNull();
    expect(textOf(fixture)).not.toContain('Time per question');
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

  describe("downloads of the candidate's own analytics", () => {
    it('offers a Download CSV and a Download PDF button, once there are results to download', async () => {
      const { fixture, request } = await start();
      request.flush(analytics());
      fixture.detectChanges();

      expect(exportLabels(fixture)).toEqual(['Download CSV', 'Download PDF']);
      expect(root(fixture).querySelector('.analytics-export [role="group"]')?.getAttribute('aria-label')).toBe('My performance');
    });

    it('offers no download when no result is released yet, since there is nothing to take', async () => {
      const { fixture, request } = await start();
      request.flush(analytics({ resultCount: 0, trend: [], sections: [] }));
      fixture.detectChanges();

      expect(exportLabels(fixture)).toEqual([]);
    });

    it('downloads the CSV from the candidate CSV route, under the name the server gave it', async () => {
      const { fixture, request } = await start();
      request.flush(analytics());
      fixture.detectChanges();
      const downloads = captureDownloads();

      exportButton(fixture, 'Download CSV')?.click();
      httpMock.expectOne((r) => r.url.endsWith('/v1/me/analytics/exports/csv') && r.method === 'POST').flush(
        new Blob(['Exam,Score\r\n'], { type: 'text/csv' }),
        { headers: { 'content-disposition': 'attachment; filename="candidate-analytics-20260912-0930.csv"' } },
      );
      fixture.detectChanges();

      expect(downloads).toEqual(['candidate-analytics-20260912-0930.csv']);
    });

    it('downloads the PDF from the candidate PDF route, and names the file itself when the server sends no name', async () => {
      const { fixture, request } = await start();
      request.flush(analytics());
      fixture.detectChanges();
      const downloads = captureDownloads();

      exportButton(fixture, 'Download PDF')?.click();
      httpMock.expectOne((r) => r.url.endsWith('/v1/me/analytics/exports/pdf') && r.method === 'POST').flush(new Blob(['%PDF-1.4']));
      fixture.detectChanges();

      expect(downloads).toEqual(['candidate-analytics.pdf']);
    });

    it('disables both download buttons while one runs, so a second press starts no second request', async () => {
      const { fixture, request } = await start();
      request.flush(analytics());
      fixture.detectChanges();
      captureDownloads();

      exportButton(fixture, 'Download PDF')?.click();
      fixture.detectChanges();

      const working = exportButton(fixture, 'Preparing the file…');
      expect(working?.disabled).toBe(true);
      expect(exportButton(fixture, 'Download CSV')?.disabled).toBe(true);
      exportButton(fixture, 'Download CSV')?.click();
      working?.click();
      fixture.detectChanges();

      const exports = httpMock.match((r) => r.url.includes('/v1/me/analytics/exports'));
      expect(exports).toHaveLength(1);
      exports[0].flush(new Blob(['%PDF-1.4']));
      fixture.detectChanges();

      expect(exportButton(fixture, 'Download CSV')?.disabled).toBe(false);
      expect(exportButton(fixture, 'Download PDF')?.disabled).toBe(false);
    });

    it('shows the problem the server gave when a download fails, and frees the buttons again', async () => {
      const { fixture, request } = await start();
      request.flush(analytics());
      fixture.detectChanges();

      exportButton(fixture, 'Download CSV')?.click();
      // A file request reads its error body as a blob, so the server's message is read out of it.
      httpMock.expectOne((r) => r.url.endsWith('/v1/me/analytics/exports/csv') && r.method === 'POST').flush(
        new Blob(['{"title":"server_error","detail":"The database is busy."}'], { type: 'application/problem+json' }),
        { status: 500, statusText: 'Error' },
      );

      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('The database is busy.');
      });
      expect(exportButton(fixture, 'Download CSV')?.disabled).toBe(false);
    });

    it('says nothing was exported when the failure carries no message', async () => {
      const { fixture, request } = await start();
      request.flush(analytics());
      fixture.detectChanges();

      exportButton(fixture, 'Download PDF')?.click();
      httpMock
        .expectOne((r) => r.url.endsWith('/v1/me/analytics/exports/pdf') && r.method === 'POST')
        .flush(new Blob(['not a problem']), { status: 502, statusText: 'Bad Gateway' });

      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('nothing was exported');
      });
    });
  });

  /** The labels of the download buttons, in the order they are shown. */
  function exportLabels(fixture: ComponentFixture<CandidateAnalytics>): string[] {
    return [...root(fixture).querySelectorAll<HTMLButtonElement>('.analytics-export button')].map((b) => b.textContent?.trim() ?? '');
  }

  /** The download button with this label. */
  function exportButton(fixture: ComponentFixture<CandidateAnalytics>, label: string): HTMLButtonElement | undefined {
    return [...root(fixture).querySelectorAll<HTMLButtonElement>('.analytics-export button')].find((b) => b.textContent?.trim() === label);
  }

  /**
   * Stubs the saving of a file, so each download is seen by the name it was given instead of being written to disk. Returns the names, in the
   * order the downloads were saved.
   */
  function captureDownloads(): string[] {
    const names: string[] = [];
    Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:download'), revokeObjectURL: vi.fn() });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      names.push(this.download);
    });
    return names;
  }
});
