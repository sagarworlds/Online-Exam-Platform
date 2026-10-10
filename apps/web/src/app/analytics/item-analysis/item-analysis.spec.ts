import { provideHttpClient } from '@angular/common/http';
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
      { questionId: 'q1', position: 1, text: 'What is force?', attempts: 40, correctCount: 30, difficulty: 0.75, discrimination: 0.4 },
      { questionId: 'q2', position: 2, text: 'Define work', attempts: 12, correctCount: 6, difficulty: null, discrimination: null },
      { questionId: 'q3', position: 3, text: 'Unit of power', attempts: 40, correctCount: 0, difficulty: 0, discrimination: -0.1 },
    ],
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

    const rows = root(fixture).querySelectorAll('tbody tr');
    expect(rows).toHaveLength(3);
    expect(rows[0].textContent).toContain('75%');
    expect(rows[0].textContent).toContain('+0.40');
    expect(rows[2].textContent).toContain('-0.10');
    // A question with fewer candidates than the minimum has no figures to show, only the counts.
    expect(rows[1].textContent).toContain('Not shown');
    expect(rows[1].textContent).toContain('12');
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
    let firstRow = root(fixture).querySelector('tbody tr td:first-child')?.textContent?.trim();
    expect(firstRow).toBe('3');

    discrimination?.click();
    fixture.detectChanges();
    firstRow = root(fixture).querySelector('tbody tr td:first-child')?.textContent?.trim();
    expect(firstRow).toBe('1');
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
});
