import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { AttemptResultDto } from '../candidate.models';
import { AttemptResult } from './attempt-result';

describe('AttemptResult', () => {
  let httpMock: HttpTestingController;

  // Two sections: one with a right answer and a skipped one, one with a wrong answer, with negative marking so the marks differ.
  const result = (overrides: Partial<AttemptResultDto> = {}): AttemptResultDto => ({
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
    partialCount: 0,
    unansweredCount: 1,
    sections: [
      { id: 's1', name: 'Algebra', score: 3.75, correctCount: 1, wrongCount: 0, partialCount: 0, unansweredCount: 1 },
      { id: 's2', name: 'Geometry', score: -1, correctCount: 0, wrongCount: 1, partialCount: 0, unansweredCount: 0 },
    ],
    rank: 2,
    percentile: 66.66,
    cohortSize: 4,
    provisional: false,
    resultVersion: 1,
    ...overrides,
  });

  const root = (fixture: ComponentFixture<AttemptResult>) => fixture.nativeElement as HTMLElement;
  const textOf = (fixture: ComponentFixture<AttemptResult>) => root(fixture).textContent ?? '';

  async function open(body: AttemptResultDto | { error: object; status: number }) {
    await TestBed.configureTestingModule({
      imports: [AttemptResult],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ attemptId: 'a1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

    const fixture = TestBed.createComponent(AttemptResult);
    fixture.detectChanges();
    const request = httpMock.expectOne((r) => r.url.endsWith('/v1/me/attempts/a1/result') && r.method === 'GET');
    if ('error' in body) {
      request.flush(body.error, { status: body.status, statusText: 'Conflict' });
    } else {
      request.flush(body);
    }
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('shows the score, the rank among the results and the percentile', async () => {
    const fixture = await open(result());

    const text = textOf(fixture);
    expect(text).toContain('Maths Final');
    expect(text).toContain('2.75 / 12');
    expect(text).toContain('Rank 2 of 4');
    expect(text).toContain('66.66%');
    expect(text).toContain('4 results');
  });

  it('shows the marks and the answers of each section, with the sign of the marks', async () => {
    const fixture = await open(result());

    const sections = root(fixture).querySelectorAll('.result-section');
    expect(sections.length).toBe(2);
    expect(sections[0].textContent).toContain('Algebra');
    expect(sections[0].textContent).toContain('+3.75');
    expect(sections[1].textContent).toContain('Geometry');
    expect(sections[1].textContent).toContain('-1');
  });

  it('says the rank may still move only while the exam is open', async () => {
    const fixture = await open(result({ provisional: true }));
    expect(textOf(fixture)).toContain('the rank can still change');
  });

  it('does not say the rank may move once the exam has closed', async () => {
    const fixture = await open(result({ provisional: false }));
    expect(textOf(fixture)).not.toContain('the rank can still change');
  });

  it('gives the API reason when the results are not released yet, and shows no score', async () => {
    const fixture = await open({
      status: 409,
      error: { title: 'results_not_released', detail: 'The correct answers have not been released yet. They will be shown when the exam’s organiser releases them.' },
    });

    expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('have not been released yet');
    expect(root(fixture).querySelector('.result-score')).toBeNull();
  });

  it('reads in the language the candidate chose', async () => {
    const fixture = await open(result());

    await TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('खंड के अनुसार');
    expect(textOf(fixture)).toContain('4 में से रैंक 2');
  });
});
