import { provideHttpClient } from '@angular/common/http';
import { vi } from 'vitest';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { ExamItemAnalysisDto } from '../analytics.models';
import { ItemAnalysis } from './item-analysis';

describe('ItemAnalysis', () => {
  let httpMock: HttpTestingController;

  const analysis = (overrides: Partial<ExamItemAnalysisDto> = {}): ExamItemAnalysisDto => ({
    examId: 'e1',
    examName: 'Physics Final',
    resultsReleased: true,
    candidateCount: 40,
    minimumCohortSize: 30,
    groupSize: 11,
    questions: [
      {
        questionId: 'q1',
        position: 1,
        text: 'What is force?',
        attempts: 40,
        correctCount: 30,
        difficulty: 0.75,
        discrimination: 0.4,
        time: { candidatesTimed: 40, averageSeconds: 42.5, status: 'Shown' },
        dropOff: { reached: 40, droppedOff: 3, rate: 0.075 },
      },
      {
        questionId: 'q2',
        position: 2,
        text: 'Define work',
        attempts: 12,
        correctCount: 6,
        difficulty: null,
        discrimination: null,
        time: { candidatesTimed: 12, averageSeconds: null, status: 'BelowMinimumCohort' },
        dropOff: { reached: 12, droppedOff: 4, rate: null },
      },
      {
        questionId: 'q3',
        position: 3,
        text: 'Unit of power',
        attempts: 40,
        correctCount: 0,
        difficulty: 0,
        discrimination: -0.1,
        time: { candidatesTimed: 40, averageSeconds: 18.2, status: 'Shown' },
        dropOff: { reached: 40, droppedOff: 0, rate: 0 },
      },
    ],
    time: { questionsTimed: 92, averageSeconds: 33.1 },
    scores: {
      count: 40,
      meanPercentOfMarks: 58.4,
      medianPercentOfMarks: 60,
      // Ten bands of ten percentage points, from the bottom; the counts add up to the 40 candidates.
      distribution: [0, 1, 2, 3, 5, 7, 9, 8, 4, 1].map((count, index) => ({
        fromPercent: index === 0 ? null : index * 10,
        toPercent: index === 9 ? null : (index + 1) * 10,
        count,
      })),
    },
    ...overrides,
  });

  const root = (fixture: ComponentFixture<ItemAnalysis>) => fixture.nativeElement as HTMLElement;
  const textOf = (fixture: ComponentFixture<ItemAnalysis>) => root(fixture).textContent ?? '';

  async function start(): Promise<{ fixture: ComponentFixture<ItemAnalysis>; request: TestRequest }> {
    await TestBed.configureTestingModule({
      imports: [ItemAnalysis],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'e1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

    const fixture = TestBed.createComponent(ItemAnalysis);
    fixture.detectChanges();
    const request = httpMock.expectOne((r) => r.url.endsWith('/v1/exams/e1/analytics/items') && r.method === 'GET');
    return { fixture, request };
  }

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('shows a loading notice while the analysis is read', async () => {
    const { fixture, request } = await start();

    expect(root(fixture).querySelector('[role="status"]')?.textContent).toContain('Loading');
    request.flush(analysis());
  });

  it('shows each question with its figures, and says so where a figure is not shown', async () => {
    const { fixture, request } = await start();

    request.flush(analysis());
    fixture.detectChanges();

    const rows = root(fixture).querySelectorAll('.item-table tbody tr');
    expect(rows).toHaveLength(3);
    expect(rows[0].textContent).toContain('75%');
    expect(rows[0].textContent).toContain('+0.40');
    expect(rows[2].textContent).toContain('-0.10');
    // A question with fewer candidates than the minimum has no figures to show, only the counts.
    expect(rows[1].textContent).toContain('Not shown');
    expect(rows[1].textContent).toContain('12');
  });

  it('shows each question\'s average time and where candidates stopped, and says why a figure is not shown', async () => {
    const { fixture, request } = await start();
    request.flush(analysis());
    fixture.detectChanges();

    const section = root(fixture).querySelector('.item-analysis__stats') as HTMLElement;
    expect(section.textContent).toContain('33.1 s');

    const rows = root(fixture).querySelectorAll<HTMLTableRowElement>('.item-table tbody tr');
    // Columns: No., Question, Attempts, Correct, Difficulty, Discrimination, Average time, Reached, Dropped off.
    const cellsOf = (row: HTMLTableRowElement) => [...row.querySelectorAll('td')].map((td) => td.textContent?.trim());
    expect(cellsOf(rows[0]).slice(6)).toEqual(['42.5 s', '40', '3 (8%)']);
    // The count of who stopped still shows, while its share is withheld with the words, never a blank.
    expect(cellsOf(rows[1]).slice(6)).toEqual(['Not shown', '12', '4 (Not shown)']);
    expect(cellsOf(rows[2]).slice(6)).toEqual(['18.2 s', '40', '0 (0%)']);
  });

  it('sorts by the share who stopped, with a withheld share last, and by the average time', async () => {
    const { fixture, request } = await start();
    request.flush(analysis());
    fixture.detectChanges();
    const sortBy = (label: string) =>
      [...root(fixture).querySelectorAll<HTMLButtonElement>('.item-analysis__sort-button')].find((b) => b.textContent?.includes(label))?.click();
    const firstPosition = () => root(fixture).querySelector('.item-table tbody tr td:first-child')?.textContent?.trim();

    sortBy('Drop-off share');
    fixture.detectChanges();
    // Shares 0%, then 8%, and question 2 (no share shown) last.
    expect(firstPosition()).toBe('3');
    expect(root(fixture).querySelectorAll('.item-table tbody tr td:first-child')[2]?.textContent?.trim()).toBe('2');

    sortBy('Average time');
    fixture.detectChanges();
    // Times: 18.2 s, then 42.5 s, and the withheld average last.
    expect(firstPosition()).toBe('3');
    expect(root(fixture).querySelectorAll('.item-table tbody tr td:first-child')[2]?.textContent?.trim()).toBe('2');
  });

  it('sorts by a column, and turns the direction over when the same column is chosen again', async () => {
    const { fixture, request } = await start();
    request.flush(analysis());
    fixture.detectChanges();

    const discrimination = [...root(fixture).querySelectorAll<HTMLButtonElement>('.item-analysis__sort-button')].find((b) =>
      b.textContent?.includes('Discrimination'),
    );
    discrimination?.click();
    fixture.detectChanges();
    let firstRow = root(fixture).querySelector('.item-table tbody tr td:first-child')?.textContent?.trim();
    expect(firstRow).toBe('3');

    discrimination?.click();
    fixture.detectChanges();
    firstRow = root(fixture).querySelector('.item-table tbody tr td:first-child')?.textContent?.trim();
    expect(firstRow).toBe('1');
  });

  it('shows the mean and the median of the candidates\' scores, and each band with its count', async () => {
    const { fixture, request } = await start();

    request.flush(analysis());
    fixture.detectChanges();

    const section = root(fixture).querySelector('.item-analysis__scores') as HTMLElement;
    expect(section.textContent).toContain('58.4%');
    expect(section.textContent).toContain('60%');
    const bands = section.querySelectorAll('.item-distribution tbody tr');
    expect(bands).toHaveLength(10);
    // The open ends of the scale are named in words, so the bottom and top bands read as "below" and "and above".
    expect(bands[0].textContent).toContain('Below 10%');
    expect(bands[1].textContent).toContain('10% to under 20%');
    expect(bands[9].textContent).toContain('90% and above');
    expect(bands[6].querySelector('.item-distribution__value')?.textContent?.trim()).toBe('9');
  });

  it('draws each bar in proportion to the largest band, so the tallest band fills the track', async () => {
    const { fixture, request } = await start();

    request.flush(analysis());
    fixture.detectChanges();

    const bars = [...root(fixture).querySelectorAll<HTMLElement>('.item-distribution__bar')];
    // The largest band holds 9 candidates, so it is full; an empty band draws nothing.
    expect(bars[6].style.width).toBe('100%');
    expect(bars[0].style.width).toBe(`${(0 / 9) * 100}%`);
    expect(bars[1].style.width).toBe(`${(1 / 9) * 100}%`);
  });

  it('says there are no scores yet, rather than a mean, when no candidate has a released result', async () => {
    const { fixture, request } = await start();

    request.flush(
      analysis({
        candidateCount: 0,
        scores: { count: 0, meanPercentOfMarks: null, medianPercentOfMarks: null, distribution: [] },
      }),
    );
    fixture.detectChanges();

    const section = root(fixture).querySelector('.item-analysis__scores') as HTMLElement;
    expect(section.textContent).toContain('there are no scores');
    expect(section.querySelector('table')).toBeNull();
  });

  it('says the results are held when the author has not released them', async () => {
    const { fixture, request } = await start();

    request.flush(analysis({ resultsReleased: false, questions: [], candidateCount: 0, groupSize: 0 }));
    fixture.detectChanges();

    expect(root(fixture).querySelector('[role="status"]')?.textContent).toContain('held');
    expect(root(fixture).querySelector('table')).toBeNull();
  });

  it('says there is nothing to analyse when the released results have no questions', async () => {
    const { fixture, request } = await start();

    request.flush(analysis({ questions: [], candidateCount: 0 }));
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('No question of this exam');
  });

  it('shows a problem and offers to try again, which reads the analysis once more', async () => {
    const { fixture, request } = await start();

    request.flush({ title: 'Forbidden', detail: 'You may not read this exam.' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('You may not read this exam.');
    root(fixture).querySelector<HTMLButtonElement>('button.btn')?.click();
    httpMock.expectOne((r) => r.url.endsWith('/v1/exams/e1/analytics/items')).flush(analysis());
    fixture.detectChanges();

    expect(root(fixture).querySelector('table')).not.toBeNull();
  });

  it('offers a Download CSV and a Download PDF button, each named for the file it downloads', async () => {
    const { fixture, request } = await start();
    request.flush(analysis());
    fixture.detectChanges();

    const labels = [...root(fixture).querySelectorAll<HTMLButtonElement>('.item-analysis__export button')].map((b) => b.textContent?.trim());
    expect(labels).toEqual(['Download CSV', 'Download PDF']);
    expect(root(fixture).querySelector('.item-analysis__export [role="group"]')?.getAttribute('aria-label')).toBe('Item analysis');
  });

  it('puts the downloads in the page header with the refresh icon, and offers none before there are figures', async () => {
    const { fixture, request } = await start();
    expect(root(fixture).querySelector('.page-header .item-analysis__export')).toBeNull();

    request.flush(analysis());
    fixture.detectChanges();

    const actions = root(fixture).querySelector('.page-header__actions')!;
    expect(actions.querySelectorAll('.item-analysis__export button')).toHaveLength(2);
    expect(actions.querySelector('app-refresh-button')).not.toBeNull();
    expect(root(fixture).querySelector('.page-header ~ .item-analysis__export')).toBeNull();
  });

  it('downloads the CSV under the name the server gave it, after recording nothing itself', async () => {
    const { fixture, request } = await start();
    request.flush(analysis());
    fixture.detectChanges();
    const downloads = captureDownloads();

    exportButton(fixture, 'Download CSV')?.click();
    httpMock.expectOne((r) => r.url.endsWith('/v1/exams/e1/analytics/items/exports') && r.method === 'POST').flush(
      new Blob(['No.,Question\r\n'], { type: 'text/csv' }),
      { headers: { 'content-disposition': 'attachment; filename="item-analysis-physics-final-20261010-0930.csv"' } },
    );
    fixture.detectChanges();

    expect(downloads).toEqual(['item-analysis-physics-final-20261010-0930.csv']);
  });

  it('downloads the PDF from its own route, under the name the server gave it', async () => {
    const { fixture, request } = await start();
    request.flush(analysis());
    fixture.detectChanges();
    const downloads = captureDownloads();

    exportButton(fixture, 'Download PDF')?.click();
    httpMock.expectOne((r) => r.url.endsWith('/v1/exams/e1/analytics/items/exports/pdf') && r.method === 'POST').flush(
      new Blob(['%PDF-1.4'], { type: 'application/pdf' }),
      { headers: { 'content-disposition': 'attachment; filename=item-analysis-physics-final-20261010-0930.pdf' } },
    );
    fixture.detectChanges();

    expect(downloads).toEqual(['item-analysis-physics-final-20261010-0930.pdf']);
  });

  it('names the file itself when the server sends no file name', async () => {
    const { fixture, request } = await start();
    request.flush(analysis());
    fixture.detectChanges();
    const downloads = captureDownloads();

    exportButton(fixture, 'Download PDF')?.click();
    httpMock.expectOne((r) => r.url.endsWith('/v1/exams/e1/analytics/items/exports/pdf') && r.method === 'POST').flush(new Blob(['%PDF-1.4']));
    fixture.detectChanges();

    expect(downloads).toEqual(['item-analysis.pdf']);
  });

  it('disables both download buttons while one runs, so a second press starts no second request', async () => {
    const { fixture, request } = await start();
    request.flush(analysis());
    fixture.detectChanges();
    captureDownloads();

    exportButton(fixture, 'Download CSV')?.click();
    fixture.detectChanges();

    const working = exportButton(fixture, 'Preparing the file…');
    expect(working?.disabled).toBe(true);
    expect(exportButton(fixture, 'Download PDF')?.disabled).toBe(true);
    exportButton(fixture, 'Download PDF')?.click();
    working?.click();
    fixture.detectChanges();

    const exports = httpMock.match((r) => r.url.includes('/analytics/items/exports'));
    expect(exports).toHaveLength(1);
    exports[0].flush(new Blob(['No.,Question\r\n']));
    fixture.detectChanges();

    expect(exportButton(fixture, 'Download CSV')?.disabled).toBe(false);
    expect(exportButton(fixture, 'Download PDF')?.disabled).toBe(false);
  });

  it('shows the problem the server gave when a download is refused, and frees the buttons again', async () => {
    const { fixture, request } = await start();
    request.flush(analysis());
    fixture.detectChanges();

    exportButton(fixture, 'Download PDF')?.click();
    // A file request reads its error body as a blob, so the server's message is read out of it.
    httpMock.expectOne((r) => r.url.endsWith('/v1/exams/e1/analytics/items/exports/pdf') && r.method === 'POST').flush(
      new Blob(['{"title":"forbidden","detail":"You may not export this exam."}'], { type: 'application/problem+json' }),
      { status: 403, statusText: 'Forbidden' },
    );

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('You may not export this exam.');
    });
    expect(exportButton(fixture, 'Download PDF')?.disabled).toBe(false);
  });

  it('says the export did not happen when the server refuses it with no message, rather than offering a file', async () => {
    const { fixture, request } = await start();
    request.flush(analysis());
    fixture.detectChanges();

    exportButton(fixture, 'Download CSV')?.click();
    // The export asks for a file, so the server's error body reaches the page as a blob.
    httpMock
      .expectOne((r) => r.url.endsWith('/v1/exams/e1/analytics/items/exports') && r.method === 'POST')
      .flush(new Blob(['{"title":"Export refused"}'], { type: 'application/problem+json' }), { status: 500, statusText: 'Error' });

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('nothing was exported');
    });
  });

  describe('the refresh icon', () => {
    const isRead = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/exams/e1/analytics/items');
    const icon = (fixture: ComponentFixture<ItemAnalysis>) => root(fixture).querySelector<HTMLButtonElement>('app-refresh-button button')!;
    const firstQuestionRow = (fixture: ComponentFixture<ItemAnalysis>) => root(fixture).querySelector('.item-table tbody tr td:first-child')?.textContent?.trim();

    it('sits in the page header, and turns while the first read is on its way', async () => {
      const { fixture, request } = await start();

      expect(root(fixture).querySelector('.page-header app-refresh-button')).not.toBeNull();
      expect(icon(fixture).getAttribute('aria-busy')).toBe('true');
      request.flush(analysis());
      fixture.detectChanges();
      expect(icon(fixture).hasAttribute('aria-busy')).toBe(false);
    });

    it('reads the analysis again without blanking the figures, and shows what changed, keeping the sort', async () => {
      const { fixture, request } = await start();
      request.flush(analysis());
      fixture.detectChanges();
      [...root(fixture).querySelectorAll<HTMLButtonElement>('.item-analysis__sort-button')].find((b) => b.textContent?.includes('Discrimination'))?.click();
      fixture.detectChanges();
      expect(firstQuestionRow(fixture)).toBe('3');

      icon(fixture).click();
      fixture.detectChanges();
      expect(icon(fixture).getAttribute('aria-busy')).toBe('true');
      expect(root(fixture).querySelector('table')).not.toBeNull();
      expect(textOf(fixture)).not.toContain('Loading');

      httpMock.expectOne(isRead).flush(analysis({ candidateCount: 55 }));
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('55');
      expect(firstQuestionRow(fixture)).toBe('3');
      expect(icon(fixture).hasAttribute('aria-busy')).toBe(false);
    });

    it('keeps the figures and says why above them when the read fails, and clears that once it works', async () => {
      const { fixture, request } = await start();
      request.flush(analysis());
      fixture.detectChanges();

      icon(fixture).click();
      httpMock.expectOne(isRead).flush({ title: 'Server error', detail: 'The analysis is not available.' }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(root(fixture).querySelector('table')).not.toBeNull();
      expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('The analysis is not available.');
      expect(icon(fixture).hasAttribute('aria-busy')).toBe(false);

      icon(fixture).click();
      httpMock.expectOne(isRead).flush(analysis());
      fixture.detectChanges();
      expect(root(fixture).querySelector('[role="alert"]')).toBeNull();
    });

    it('moves on from "results are held" when the author has released them since', async () => {
      const { fixture, request } = await start();
      request.flush(analysis({ resultsReleased: false, questions: [] }));
      fixture.detectChanges();
      expect(root(fixture).querySelector('table')).toBeNull();

      icon(fixture).click();
      httpMock.expectOne(isRead).flush(analysis());
      fixture.detectChanges();

      expect(root(fixture).querySelector('table')).not.toBeNull();
    });

    it('is the retry when the first read failed, showing the failure once, in its own card', async () => {
      const { fixture, request } = await start();
      request.flush({ title: 'Forbidden', detail: 'You may not read this exam.' }, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();
      expect(root(fixture).querySelectorAll('[role="alert"]')).toHaveLength(1);

      icon(fixture).click();
      httpMock.expectOne(isRead).flush(analysis());
      fixture.detectChanges();

      expect(root(fixture).querySelector('table')).not.toBeNull();
      expect(root(fixture).querySelector('[role="alert"]')).toBeNull();
    });
  });

  /** The download button of the export row with this label. */
  function exportButton(fixture: ComponentFixture<ItemAnalysis>, label: string): HTMLButtonElement | undefined {
    return [...root(fixture).querySelectorAll<HTMLButtonElement>('.item-analysis__export button')].find((b) => b.textContent?.trim() === label);
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
