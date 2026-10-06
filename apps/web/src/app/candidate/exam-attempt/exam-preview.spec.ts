import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { afterEach, beforeEach, vi } from 'vitest';
import { CandidateApiService } from '../candidate-api.service';
import { AttemptDto } from '../candidate.models';
import { ExamAttempt } from './exam-attempt';
import { PreviewCandidateApiService } from './preview-candidate-api.service';

/** The exam page as staff see it on the preview route (FR-15): the same page, over an API that saves nothing. */
describe('ExamAttempt preview', () => {
  const NOW = Date.parse('2026-10-05T04:30:00Z');
  let httpMock: HttpTestingController;

  const exam = (overrides: Partial<AttemptDto> = {}): AttemptDto => ({
    id: 'generated-id',
    examId: 'e1',
    examName: 'Maths Final',
    status: 'InProgress',
    startedAtUtc: '2026-10-05T04:30:00Z',
    deadlineUtc: '2026-10-05T05:00:00Z',
    submittedAtUtc: null,
    autoSubmitted: false,
    score: null,
    maxScore: null,
    serverTimeUtc: '2026-10-05T04:30:00Z',
    sections: [
      {
        id: 's1',
        name: 'Section A',
        questions: [
          {
            id: 'q1',
            text: 'What is 2 + 2?',
            options: [
              { id: 'o1', text: '4' },
              { id: 'o2', text: '5' },
            ],
            selectedOptionId: null,
            markedForReview: false,
          },
        ],
      },
    ],
    contentProtection: true,
    focusViolationLimit: 3,
    ...overrides,
  });

  const root = (fixture: ComponentFixture<ExamAttempt>) => fixture.nativeElement as HTMLElement;
  const textOf = (fixture: ComponentFixture<ExamAttempt>) => (root(fixture).textContent ?? '').replace(/\s+/g, ' ');
  const buttonLabelled = (fixture: ComponentFixture<ExamAttempt>, label: string) =>
    Array.from(root(fixture).querySelectorAll('button')).find((b) => b.textContent?.includes(label));

  async function open(body: AttemptDto = exam()) {
    await TestBed.configureTestingModule({
      imports: [ExamAttempt],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CandidateApiService, useClass: PreviewCandidateApiService },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'e1' }), data: { preview: true } } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

    const fixture = TestBed.createComponent(ExamAttempt);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/exams/e1/preview')).flush(body);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('loads the exam from the preview endpoint by the exam id, and says it is a preview', async () => {
    const fixture = await open();

    expect(textOf(fixture)).toContain('Preview. This is how candidates see the exam.');
    expect(textOf(fixture)).toContain('Maths Final');
    expect(textOf(fixture)).toContain('What is 2 + 2?');
    const back = Array.from(root(fixture).querySelectorAll('a')).find((a) => a.textContent?.includes('Back to the exam'));
    expect(back?.getAttribute('href')).toBe('/exams/e1');
  });

  it('saves nothing: a choice, a mark and a clear all stay on the page', async () => {
    const fixture = await open();

    root(fixture).querySelectorAll<HTMLInputElement>('input[type="radio"]')[1].click();
    fixture.detectChanges();
    expect(textOf(fixture)).toContain('1 of 1 answered');

    buttonLabelled(fixture, 'Mark for review')?.click();
    fixture.detectChanges();
    buttonLabelled(fixture, 'Clear response')?.click();
    fixture.detectChanges();

    // httpMock.verify() in afterEach fails the test if any request other than the preview load was made.
    expect(textOf(fixture)).toContain('0 of 1 answered');
  });

  it('does not turn off copying, nor watch for leaving the page, even when the exam does', async () => {
    const fixture = await open();

    const copy = new Event('copy', { bubbles: true, cancelable: true });
    document.body.dispatchEvent(copy);
    window.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(copy.defaultPrevented).toBe(false);
    expect(document.body.classList).not.toContain('exam-protected');
    expect(textOf(fixture)).not.toContain('Stay on this page');
    expect(textOf(fixture)).not.toContain('turned off during this exam');
  });

  it('asks the server nothing while it runs, and does not reload when the countdown reaches zero', async () => {
    const fixture = await open();

    vi.advanceTimersByTime(45 * 60 * 1000);
    fixture.detectChanges();

    expect(root(fixture).querySelector('.countdown')?.textContent?.trim()).toBe('0:00');
    expect(textOf(fixture)).toContain('What is 2 + 2?');
  });

  it('ends the preview on submit, going back to the exam without scoring anything', async () => {
    const fixture = await open();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    buttonLabelled(fixture, 'Submit exam')?.click();
    fixture.detectChanges();
    buttonLabelled(fixture, 'Yes, submit')?.click();

    expect(navigate).toHaveBeenCalledWith(['/exams', 'e1']);
  });
});
