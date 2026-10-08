import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { I18nService } from '../../i18n/i18n.service';
import { MyIssueReportDto } from '../candidate.models';
import { ReportIssue } from './report-issue';

describe('ReportIssue (FR-42)', () => {
  let httpMock: HttpTestingController;

  const created = (overrides: Partial<MyIssueReportDto> = {}): MyIssueReportDto => ({
    id: 'r1',
    category: 'Question',
    questionId: 'q1',
    message: 'Option C is missing',
    reportedAtUtc: '2026-10-08T10:00:00Z',
    ...overrides,
  });

  async function open(questionId: string | null = 'q1'): Promise<ComponentFixture<ReportIssue>> {
    const fixture = TestBed.createComponent(ReportIssue);
    fixture.componentRef.setInput('attemptId', 'a1');
    fixture.componentRef.setInput('questionId', questionId);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  const root = (fixture: ComponentFixture<ReportIssue>) => fixture.nativeElement as HTMLElement;
  const textOf = (fixture: ComponentFixture<ReportIssue>) => root(fixture).textContent ?? '';
  const button = (fixture: ComponentFixture<ReportIssue>, label: string) =>
    Array.from(root(fixture).querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const box = (fixture: ComponentFixture<ReportIssue>) => root(fixture).querySelector('textarea') as HTMLTextAreaElement;
  const kind = (fixture: ComponentFixture<ReportIssue>) => root(fixture).querySelector('select') as HTMLSelectElement;
  const isReportPost = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/me/attempts/a1/issues');

  const type = (fixture: ComponentFixture<ReportIssue>, value: string) => {
    box(fixture).value = value;
    box(fixture).dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  const choose = (fixture: ComponentFixture<ReportIssue>, value: string) => {
    kind(fixture).value = value;
    kind(fixture).dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };

  /** Opens the form and types a description in it. */
  const fillIn = (fixture: ComponentFixture<ReportIssue>, message = 'Option C is missing') => {
    button(fixture, 'Report an issue').click();
    fixture.detectChanges();
    type(fixture, message);
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('offers a button and no form until it is pressed', async () => {
    const fixture = await open();

    expect(button(fixture, 'Report an issue')).toBeTruthy();
    expect(box(fixture)).toBeNull();
  });

  it('opens a form with the three kinds of problem, leaning to "a problem with this question" when one is on screen', async () => {
    const fixture = await open('q1');

    button(fixture, 'Report an issue').click();
    fixture.detectChanges();

    expect(Array.from(kind(fixture).options).map((o) => o.value)).toEqual(['Question', 'Technical', 'Other']);
    expect(kind(fixture).value).toBe('Question');
    expect(textOf(fixture)).toContain('A problem with this question');
    expect(textOf(fixture)).toContain('A technical problem');
    expect(textOf(fixture)).toContain('Something else');
  });

  it('leans to "a technical problem" when no question is on screen', async () => {
    const fixture = await open(null);

    button(fixture, 'Report an issue').click();
    fixture.detectChanges();

    expect(kind(fixture).value).toBe('Technical');
  });

  it('will not send an empty description', async () => {
    const fixture = await open();
    fillIn(fixture, '   ');

    expect(button(fixture, 'Send report').disabled).toBe(true);

    button(fixture, 'Send report').click();
    httpMock.expectNone(isReportPost);
  });

  it('sends the kind, the words and the question on screen, then closes the form and says the report went through', async () => {
    const fixture = await open('q1');
    fillIn(fixture, '  Option C is missing  ');

    button(fixture, 'Send report').click();
    fixture.detectChanges();

    const request = httpMock.expectOne(isReportPost);
    expect(request.request.body).toEqual({ category: 'Question', message: 'Option C is missing', questionId: 'q1' });
    request.flush(created(), { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(box(fixture)).toBeNull();
    const confirmation = root(fixture).querySelector('[role="status"]');
    expect(confirmation?.textContent).toContain('Your report was sent.');
    expect(confirmation?.textContent).toContain('You can carry on with your exam.');
  });

  it('names no question for a report about the page or anything else, even when a question is on screen', async () => {
    const fixture = await open('q1');
    fillIn(fixture, 'The timer froze');
    choose(fixture, 'Technical');

    button(fixture, 'Send report').click();

    const request = httpMock.expectOne(isReportPost);
    expect(request.request.body).toEqual({ category: 'Technical', message: 'The timer froze', questionId: null });
    request.flush(created({ category: 'Technical', questionId: null }), { status: 201, statusText: 'Created' });
  });

  it('cannot be sent twice while it is on its way', async () => {
    const fixture = await open();
    fillIn(fixture);

    button(fixture, 'Send report').click();
    fixture.detectChanges();

    expect(button(fixture, 'Send report').disabled).toBe(true);
    expect(box(fixture).disabled).toBe(true);
    httpMock.expectOne(isReportPost).flush(created(), { status: 201, statusText: 'Created' });
  });

  it('puts the form away without sending anything when it is cancelled', async () => {
    const fixture = await open();
    fillIn(fixture);

    button(fixture, 'Cancel').click();
    fixture.detectChanges();

    expect(box(fixture)).toBeNull();
    expect(button(fixture, 'Report an issue')).toBeTruthy();
    httpMock.expectNone(isReportPost);
  });

  it('starts the next report empty, and no longer says the last one went through', async () => {
    const fixture = await open();
    fillIn(fixture);
    button(fixture, 'Send report').click();
    httpMock.expectOne(isReportPost).flush(created(), { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    button(fixture, 'Report an issue').click();
    fixture.detectChanges();

    expect(box(fixture).value).toBe('');
    expect(textOf(fixture)).not.toContain('Your report was sent.');
  });

  describe('when the API refuses', () => {
    const refuse = (fixture: ComponentFixture<ReportIssue>, body: object | null, status: number) => {
      button(fixture, 'Send report').click();
      httpMock.expectOne(isReportPost).flush(body, { status, statusText: 'Refused' });
      fixture.detectChanges();
    };

    it('keeps what the candidate wrote and says what to fix when the description is not acceptable', async () => {
      const fixture = await open();
      fillIn(fixture, 'Something');

      refuse(fixture, { title: 'invalid_attempt', detail: 'The description must be at most 1000 characters.' }, 400);

      expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('Say what is wrong, in up to 1000 characters.');
      expect(box(fixture).value).toBe('Something');
      expect(button(fixture, 'Send report').disabled).toBe(false);
    });

    it('says an attempt has reached the number of reports it may carry', async () => {
      const fixture = await open();
      fillIn(fixture);

      refuse(fixture, { title: 'too_many_issue_reports' }, 429);

      expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('as many problems as one attempt allows');
    });

    it('passes on the API’s own sentence for any other refusal, such as an attempt that is already over', async () => {
      const fixture = await open();
      fillIn(fixture);

      refuse(fixture, { title: 'attempt_not_in_progress', detail: 'This attempt has already been submitted.' }, 409);

      expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('This attempt has already been submitted.');
    });

    it('falls back to a plain sentence when the answer says nothing', async () => {
      const fixture = await open();
      fillIn(fixture);

      refuse(fixture, null, 500);

      expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain('Your report could not be sent. Please try again.');
    });
  });

  it('is read in the language the candidate chose', async () => {
    const fixture = await open();

    TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();
    expect(button(fixture, 'समस्या की रिपोर्ट करें')).toBeTruthy();

    button(fixture, 'समस्या की रिपोर्ट करें').click();
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('यह किस तरह की समस्या है?');
    expect(textOf(fixture)).toContain('इस प्रश्न में कोई समस्या');
    expect(button(fixture, 'रिपोर्ट भेजें')).toBeTruthy();
  });
});
