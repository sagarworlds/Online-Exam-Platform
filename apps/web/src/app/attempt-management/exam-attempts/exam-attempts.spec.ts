import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { AttemptSummaryDto } from '../../candidate/candidate.models';
import { ExamAttemptsDto, ExamCandidateDto } from '../attempt-admin.models';
import { ExamAttempts } from './exam-attempts';

describe('ExamAttempts', () => {
  let httpMock: HttpTestingController;

  const attempt = (number: number, status: 'InProgress' | 'Submitted', score: number | null = null, autoSubmitted = false): AttemptSummaryDto => ({
    id: `a${number}`,
    number,
    status,
    startedAtUtc: '2026-10-05T04:30:00Z',
    submittedAtUtc: status === 'Submitted' ? '2026-10-05T04:50:00Z' : null,
    autoSubmitted,
    score,
    maxScore: score === null ? null : 10,
  });

  const candidate = (overrides: Partial<ExamCandidateDto> = {}): ExamCandidateDto => ({
    candidateId: 'c1',
    email: 'amy@example.com',
    attemptsAllowed: 1,
    attemptsUsed: 1,
    canGrant: true,
    attempts: [attempt(1, 'Submitted', 4)],
    ...overrides,
  });

  const exam = (candidates: ExamCandidateDto[], windowClosed = false, attemptsPerCandidate = 1): ExamAttemptsDto => ({
    examId: 'exam-1',
    examName: 'Maths Final',
    windowClosed,
    attemptsPerCandidate,
    candidates,
  });

  const root = (fixture: ComponentFixture<ExamAttempts>) => fixture.nativeElement as HTMLElement;
  // As a person reads it: runs of whitespace in the template collapse to one space.
  const textOf = (fixture: ComponentFixture<ExamAttempts>) => (root(fixture).textContent ?? '').replace(/\s+/g, ' ');
  const buttonLabelled = (fixture: ComponentFixture<ExamAttempts>, label: string) =>
    Array.from(root(fixture).querySelectorAll('button')).filter((b) => b.textContent?.trim() === label);

  function open(body: ExamAttemptsDto | 'error') {
    const fixture = TestBed.createComponent(ExamAttempts);
    fixture.detectChanges();
    const request = httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/exams/exam-1/attempts'));
    if (body === 'error') {
      request.flush({ title: 'exam_not_found', detail: 'No exam matches the given id.' }, { status: 404, statusText: 'Not Found' });
    } else {
      request.flush(body);
    }
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExamAttempts],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'exam-1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('shows the paper of an attempt on request, marking the questions drawn for the candidate', () => {
    const fixture = open(exam([candidate()]));

    buttonLabelled(fixture, 'Show paper')[0].click();
    const request = httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/exams/exam-1/attempts/a1/paper'));
    request.flush({
      attemptId: 'a1',
      number: 1,
      hasDrawnQuestions: true,
      sections: [
        {
          id: 's1',
          name: 'Algebra',
          questions: [
            { id: 'q1', text: '<p>Fixed question</p>', drawn: false },
            { id: 'q2', text: '<p>Drawn question</p>', drawn: true },
          ],
        },
      ],
    });
    fixture.detectChanges();

    const text = textOf(fixture);
    expect(text).toContain('Fixed question');
    expect(text).toContain('Drawn question drawn');
    expect(text).toContain("other candidates' papers may differ");
    expect(buttonLabelled(fixture, 'Hide paper').length).toBe(1);
  });

  it('says so when the paper cannot be loaded', () => {
    const fixture = open(exam([candidate()]));

    buttonLabelled(fixture, 'Show paper')[0].click();
    httpMock
      .expectOne((r) => r.url.endsWith('/v1/exams/exam-1/attempts/a1/paper'))
      .flush({ title: 'attempt_not_found', detail: 'No attempt matches the given id.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(root(fixture).querySelector('[role="alert"]')).not.toBeNull();
    expect(buttonLabelled(fixture, 'Show paper').length).toBe(1);
  });

  it('lists each candidate with how many attempts they have used and what each scored', () => {
    const fixture = open(
      exam([
        candidate({ attemptsAllowed: 2, attemptsUsed: 2, canGrant: true, attempts: [attempt(1, 'Submitted', 4, true), attempt(2, 'InProgress')] }),
        candidate({ candidateId: 'c2', email: 'bob@example.com', attemptsUsed: 0, canGrant: false, attempts: [] }),
      ]),
    );

    expect(textOf(fixture)).toContain('Maths Final');
    expect(textOf(fixture)).toContain('amy@example.com');
    expect(textOf(fixture)).toContain('2 of 2 attempts used');
    expect(textOf(fixture)).toContain('Attempt 1 · 4 / 10 (ended automatically)');
    expect(textOf(fixture)).toContain('Attempt 2 · In progress');
    expect(textOf(fixture)).toContain('bob@example.com');
    expect(textOf(fixture)).toContain('Has not started the exam yet.');
  });

  it('says when nobody is enrolled', () => {
    expect(textOf(open(exam([])))).toContain('Nobody has accepted an invitation');
  });

  it('shows the reason when the exam cannot be read', () => {
    expect(textOf(open('error'))).toContain('No exam matches the given id.');
  });

  it('turns off "Give another attempt" for someone who still has one to use, and says why', () => {
    const fixture = open(exam([candidate({ attemptsUsed: 0, canGrant: false, attempts: [] })]));

    const [button] = buttonLabelled(fixture, 'Give another attempt');
    expect(button.disabled).toBe(true);
    expect(textOf(fixture)).toContain('Still has an attempt left to use.');
  });

  it("says how many attempts the exam gives every candidate, instead of always one", () => {
    expect(textOf(open(exam([candidate()], false, 1)))).toContain('Every candidate has one attempt;');
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('names the exam\'s limit when it is more than one', () => {
    const fixture = open(exam([candidate({ attemptsAllowed: 3, attemptsUsed: 1 })], false, 3));

    expect(textOf(fixture)).toContain('Every candidate has 3 attempts;');
    expect(textOf(fixture)).toContain('1 of 3 attempts used');
  });

  it('points to the exam\'s limit, not another grant, for a candidate over a lowered limit', () => {
    const fixture = open(
      exam([candidate({ attemptsAllowed: 1, attemptsUsed: 2, canGrant: false, attempts: [attempt(1, 'Submitted', 4), attempt(2, 'Submitted', 5)] })], false, 1),
    );

    expect(buttonLabelled(fixture, 'Give another attempt')[0].disabled).toBe(true);
    expect(textOf(fixture)).toContain('Has made more attempts than the exam now allows');
    expect(textOf(fixture)).toContain("Raise the exam's attempts allowed");
  });

  it('turns it off for everyone once the exam can no longer be started, and says so', () => {
    const fixture = open(exam([candidate({ canGrant: false })], true));

    expect(buttonLabelled(fixture, 'Give another attempt')[0].disabled).toBe(true);
    expect(textOf(fixture)).toContain('The exam can no longer be started');
  });

  it('asks for an optional reason first, and sends it trimmed, then shows how the candidate now stands', () => {
    const fixture = open(exam([candidate()]));
    buttonLabelled(fixture, 'Give another attempt')[0].click();
    fixture.detectChanges();
    const input = root(fixture).querySelector('input[type="text"]') as HTMLInputElement;
    input.value = '  Power cut  ';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    buttonLabelled(fixture, 'Give another attempt')[0].click();

    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/exams/exam-1/candidates/c1/extra-attempts'));
    expect(post.request.body).toEqual({ reason: 'Power cut' });
    post.flush(candidate({ attemptsAllowed: 2, canGrant: false }), { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('amy@example.com can now make attempt 2.');
    expect(textOf(fixture)).toContain('1 of 2 attempts used');
    // The form is closed and the button is off again until the new attempt has been used.
    expect(root(fixture).querySelector('input[type="text"]')).toBeNull();
    expect(buttonLabelled(fixture, 'Give another attempt')[0].disabled).toBe(true);
  });

  it('sends no reason at all when none is given', () => {
    const fixture = open(exam([candidate()]));
    buttonLabelled(fixture, 'Give another attempt')[0].click();
    fixture.detectChanges();

    buttonLabelled(fixture, 'Give another attempt')[0].click();

    const post = httpMock.expectOne((r) => r.method === 'POST');
    expect(post.request.body).toEqual({ reason: null });
    post.flush(candidate({ attemptsAllowed: 2, canGrant: false }), { status: 201, statusText: 'Created' });
  });

  it('can be cancelled without sending anything', () => {
    const fixture = open(exam([candidate()]));
    buttonLabelled(fixture, 'Give another attempt')[0].click();
    fixture.detectChanges();

    buttonLabelled(fixture, 'Cancel')[0].click();
    fixture.detectChanges();

    expect(root(fixture).querySelector('input[type="text"]')).toBeNull();
  });

  it('opens the form for one candidate at a time, so a stray click cannot give two', () => {
    const fixture = open(exam([candidate(), candidate({ candidateId: 'c2', email: 'bob@example.com' })]));

    buttonLabelled(fixture, 'Give another attempt')[0].click();
    fixture.detectChanges();
    expect(root(fixture).querySelectorAll('input[type="text"]').length).toBe(1);
    buttonLabelled(fixture, 'Give another attempt').find((b) => !b.classList.contains('primary'))?.click();
    fixture.detectChanges();

    expect(root(fixture).querySelectorAll('input[type="text"]').length).toBe(1);
  });

  it('shows the API’s reason when another attempt is refused, and keeps the form open to try again', () => {
    const fixture = open(exam([candidate()]));
    buttonLabelled(fixture, 'Give another attempt')[0].click();
    fixture.detectChanges();
    buttonLabelled(fixture, 'Give another attempt')[0].click();

    httpMock
      .expectOne((r) => r.method === 'POST')
      .flush({ title: 'attempt_available', detail: 'This candidate still has an attempt left; another can be given once they have used it.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('still has an attempt left');
    expect(root(fixture).querySelector('input[type="text"]')).not.toBeNull();
  });

  it('does not let the button be pressed again while the request is in flight', () => {
    const fixture = open(exam([candidate()]));
    buttonLabelled(fixture, 'Give another attempt')[0].click();
    fixture.detectChanges();
    buttonLabelled(fixture, 'Give another attempt')[0].click();
    fixture.detectChanges();

    expect(buttonLabelled(fixture, 'Give another attempt')[0].disabled).toBe(true);
    httpMock.expectOne((r) => r.method === 'POST').flush(candidate({ attemptsAllowed: 2, canGrant: false }), { status: 201, statusText: 'Created' });
  });

  describe('organiser actions on an attempt (FR-29)', () => {
    const isAction = (action: string) => (r: { method: string; url: string }) =>
      r.method === 'POST' && r.url.endsWith(`/v1/exams/exam-1/attempts/a1/${action}`);
    const staff = (overrides: Partial<AttemptSummaryDto> = {}): AttemptSummaryDto => ({ ...attempt(1, 'InProgress'), ...overrides });
    const withAttempt = (summary: AttemptSummaryDto) => candidate({ attempts: [summary] });
    const field = (fixture: ComponentFixture<ExamAttempts>) => root(fixture).querySelector('textarea') as HTMLTextAreaElement;
    const type = (fixture: ComponentFixture<ExamAttempts>, text: string) => {
      field(fixture).value = text;
      field(fixture).dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };

    it('offers warn, pause and end for an attempt in progress, and invalidate only once it is finished', () => {
      const open1 = open(exam([withAttempt(staff())]));
      expect(buttonLabelled(open1, 'Warn')).toHaveLength(1);
      expect(buttonLabelled(open1, 'Pause')).toHaveLength(1);
      expect(buttonLabelled(open1, 'End attempt')).toHaveLength(1);
      expect(buttonLabelled(open1, 'Invalidate result')).toHaveLength(0);
      httpMock.verify();

      const finished = open(exam([withAttempt(staff({ status: 'Submitted', score: 4, maxScore: 10 }))]));
      expect(buttonLabelled(finished, 'Invalidate result')).toHaveLength(1);
      expect(buttonLabelled(finished, 'Warn')).toHaveLength(0);
      expect(buttonLabelled(finished, 'Pause')).toHaveLength(0);
    });

    it('shows Resume instead of Pause for a paused attempt, and says it is paused', () => {
      const fixture = open(exam([withAttempt(staff({ paused: true }))]));

      expect(buttonLabelled(fixture, 'Resume')).toHaveLength(1);
      expect(buttonLabelled(fixture, 'Pause')).toHaveLength(0);
      expect(textOf(fixture)).toContain('Paused');
    });

    it('shows how often the candidate left the page and how many warnings they were sent', () => {
      const fixture = open(exam([withAttempt(staff({ focusViolations: 2, warnings: 1 }))]));

      expect(textOf(fixture)).toContain('Left the page 2 times');
      expect(textOf(fixture)).toContain('1 warning');
    });

    it('sends a warning with the typed words, then shows the updated row', () => {
      const fixture = open(exam([withAttempt(staff())]));

      buttonLabelled(fixture, 'Warn')[0].click();
      fixture.detectChanges();
      expect(buttonLabelled(fixture, 'Send warning')[0].disabled).toBe(true);
      type(fixture, '  Eyes on your own screen.  ');
      buttonLabelled(fixture, 'Send warning')[0].click();

      const request = httpMock.expectOne(isAction('warn'));
      expect(request.request.body).toEqual({ message: 'Eyes on your own screen.' });
      request.flush(staff({ warnings: 1 }));
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('Warning sent to amy@example.com.');
      expect(textOf(fixture)).toContain('1 warning');
      expect(field(fixture)).toBeNull();
    });

    it('pauses and resumes without asking for words', () => {
      const fixture = open(exam([withAttempt(staff())]));

      buttonLabelled(fixture, 'Pause')[0].click();
      httpMock.expectOne(isAction('pause')).flush(staff({ paused: true }));
      fixture.detectChanges();
      expect(textOf(fixture)).toContain('Attempt 1 of amy@example.com is paused.');

      buttonLabelled(fixture, 'Resume')[0].click();
      httpMock.expectOne(isAction('resume')).flush(staff());
      fixture.detectChanges();
      expect(textOf(fixture)).toContain('was resumed');
      expect(buttonLabelled(fixture, 'Pause')).toHaveLength(1);
    });

    it('ends an attempt only with a reason, and shows it as ended by an organiser', () => {
      const fixture = open(exam([withAttempt(staff())]));

      buttonLabelled(fixture, 'End attempt')[0].click();
      fixture.detectChanges();
      expect(buttonLabelled(fixture, 'End attempt')).toHaveLength(2);
      const confirm = buttonLabelled(fixture, 'End attempt')[1];
      expect(confirm.disabled).toBe(true);
      type(fixture, 'Caught using a phone.');
      confirm.click();

      const request = httpMock.expectOne(isAction('terminate'));
      expect(request.request.body).toEqual({ reason: 'Caught using a phone.' });
      request.flush(staff({ status: 'Submitted', score: 3, maxScore: 10, autoSubmitted: true, terminatedByAdmin: true, terminationReason: 'Caught using a phone.' }));
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('3 / 10 (ended by an administrator)');
      expect(buttonLabelled(fixture, 'Invalidate result')).toHaveLength(1);
    });

    it('invalidates a finished result with a reason, keeps showing the score to staff, and offers it only once', () => {
      const fixture = open(exam([withAttempt(staff({ status: 'Submitted', score: 4, maxScore: 10 }))]));

      buttonLabelled(fixture, 'Invalidate result')[0].click();
      fixture.detectChanges();
      type(fixture, 'Answers were shared.');
      buttonLabelled(fixture, 'Invalidate result')[1].click();

      const request = httpMock.expectOne(isAction('invalidate'));
      expect(request.request.body).toEqual({ reason: 'Answers were shared.' });
      request.flush(staff({ status: 'Submitted', score: 4, maxScore: 10, invalidated: true, invalidationReason: 'Answers were shared.' }));
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('4 / 10');
      expect(textOf(fixture)).toContain('Invalidated');
      expect(buttonLabelled(fixture, 'Invalidate result')).toHaveLength(0);
    });

    it('sends nothing for blank words, and Cancel closes the form', () => {
      const fixture = open(exam([withAttempt(staff())]));
      buttonLabelled(fixture, 'Warn')[0].click();
      fixture.detectChanges();
      type(fixture, '   ');

      buttonLabelled(fixture, 'Send warning')[0].click();
      buttonLabelled(fixture, 'Cancel')[0].click();
      fixture.detectChanges();

      expect(field(fixture)).toBeNull();
    });

    it('says why an action failed and leaves the row as it was', () => {
      const fixture = open(exam([withAttempt(staff())]));

      buttonLabelled(fixture, 'Pause')[0].click();
      httpMock.expectOne(isAction('pause')).flush({ title: 'attempt_not_in_progress', detail: 'This attempt has already been submitted.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(textOf(fixture)).toContain('This attempt has already been submitted.');
      expect(buttonLabelled(fixture, 'Pause')).toHaveLength(1);
    });
  });
});
