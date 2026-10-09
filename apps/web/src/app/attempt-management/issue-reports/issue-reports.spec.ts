import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LANGUAGE_STORAGE_KEY } from '../../i18n/i18n.service';
import { HI } from '../../i18n/messages.hi';
import { MR } from '../../i18n/messages.mr';
import { IssueReportRow } from '../attempt-admin.models';
import { IssueReports } from './issue-reports';

const row = (id: string, overrides: Partial<IssueReportRow> = {}): IssueReportRow => ({
  id,
  examId: 'e1',
  examName: 'Maths Final',
  attemptId: `a-${id}`,
  attemptNumber: 1,
  candidateId: `c-${id}`,
  candidateEmail: `${id}@example.com`,
  questionId: 'q1',
  questionText: '<p>Capital of France?</p>',
  category: 'Question',
  message: 'Option C is missing',
  reportedAtUtc: '2026-10-05T05:00:00Z',
  status: 'Open',
  resolvedAtUtc: null,
  resolutionNote: null,
  ...overrides,
});

describe('IssueReports (FR-42)', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<IssueReports>;
  let root: HTMLElement;

  const isList = (r: { method: string; url: string }) =>
    r.method === 'GET' && r.url.includes('/v1/issue-reports');
  const isResolve = (id: string) => (r: { method: string; url: string }) =>
    r.method === 'POST' && r.url.endsWith(`/v1/issue-reports/${id}/resolve`);

  async function open(rows: IssueReportRow[] | 'error') {
    await TestBed.configureTestingModule({
      imports: [IssueReports],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(IssueReports);
    fixture.detectChanges();
    const list = httpMock.expectOne(isList);
    expect(list.request.params.get('status')).toBe('open');
    if (rows === 'error') {
      list.flush(
        { title: 'forbidden', detail: 'No access.' },
        { status: 403, statusText: 'Forbidden' },
      );
    } else {
      list.flush(rows);
    }
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ');
  const buttons = (label: string) =>
    Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === label);
  const press = (label: string, index = 0) => {
    buttons(label)[index].click();
    fixture.detectChanges();
  };
  const cards = () => Array.from(root.querySelectorAll<HTMLElement>('.issue-row'));
  const results = () => Array.from(root.querySelectorAll<HTMLElement>('.issue-result'));
  const type = (box: HTMLTextAreaElement, value: string) => {
    box.value = value;
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  /** Lets the page draw what the last action changed, and any focus that waits for it, to be moved. */
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('lists the open reports with who reported, in which exam and attempt, and what they wrote', async () => {
    await open([
      row('r1'),
      row('r2', {
        category: 'Technical',
        questionId: null,
        questionText: null,
        message: 'The timer froze',
        attemptNumber: 2,
      }),
    ]);

    expect(cards().length).toBe(2);
    const first = cards()[0].textContent ?? '';
    expect(first).toContain('r1@example.com');
    expect(first).toContain('Maths Final');
    expect(first).toContain('attempt 1');
    expect(first).toContain('Option C is missing');
    expect(first).toContain('Question');
    const second = cards()[1].textContent ?? '';
    expect(second).toContain('attempt 2');
    expect(second).toContain('The timer froze');
    expect(second).toContain('Technical');
  });

  it('says how many reports are waiting, in the singular for one and the plural for more', async () => {
    await open([row('r1'), row('r2')]);

    expect(root.querySelector('.issue-queue__count')?.textContent?.trim()).toBe(
      '2 reports are waiting.',
    );
  });

  it('links an exam to its candidates page', async () => {
    await open([row('r1')]);

    expect(root.querySelector('a')?.getAttribute('href')).toBe('/exams/e1/attempts');
  });

  it('shows the question on screen when the report named one, open, and nothing about a question when it did not', async () => {
    await open([row('r1'), row('r2', { questionId: null, questionText: null })]);

    expect(root.querySelector('details')).toBeNull();
    expect(cards()[0].querySelector('.issue-row__question')?.textContent).toContain(
      'Capital of France?',
    );
    expect(cards()[1].querySelector('.issue-row__question')).toBeNull();
    expect(cards()[1].textContent).not.toContain('no longer in the bank');
  });

  it('says so when the question named is no longer in the bank', async () => {
    await open([row('r1', { questionText: null })]);

    expect(text()).toContain('(question no longer in the bank)');
  });

  it('copes with a candidate or an exam that can no longer be read', async () => {
    await open([row('r1', { candidateEmail: null, examName: null, attemptNumber: null })]);

    expect(text()).toContain('A candidate who is no longer enrolled');
    expect(text()).toContain('An exam that can no longer be read');
    expect(text()).not.toContain('attempt ');
  });

  it('says so when nothing is waiting', async () => {
    await open([]);

    expect(text()).toContain('No open reports.');
  });

  it('shows why the queue could not be read', async () => {
    await open('error');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('No access.');
  });

  it('asks again for the other status when it is chosen, and shows what was resolved and what was done', async () => {
    await open([row('r1')]);

    const select = root.querySelector('select') as HTMLSelectElement;
    select.value = 'resolved';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const list = httpMock.expectOne(isList);
    expect(list.request.params.get('status')).toBe('resolved');
    list.flush([
      row('r9', {
        status: 'Resolved',
        resolvedAtUtc: '2026-10-05T06:00:00Z',
        resolutionNote: 'Added option C',
      }),
    ]);
    fixture.detectChanges();

    expect(cards().length).toBe(1);
    expect(text()).toContain('Resolved');
    expect(text()).toContain('What was done: Added option C');
    // A settled report cannot be settled again, so it offers nothing to do.
    expect(buttons('Resolve…').length).toBe(0);
  });

  it('ignores a status it does not offer', async () => {
    await open([row('r1')]);

    const select = root.querySelector('select') as HTMLSelectElement;
    select.dispatchEvent(new Event('change'));
    (
      fixture.componentInstance as unknown as { onStatusChanged(value: string): void }
    ).onStatusChanged('bogus');
    fixture.detectChanges();

    httpMock.expectNone(isList);
  });

  describe('resolving', () => {
    it('opens a form for one report at a time, with the note optional and a hint about the attempt', async () => {
      await open([row('r1'), row('r2')]);

      press('Resolve…', 0);
      expect(root.querySelectorAll('textarea').length).toBe(1);
      expect(text()).toContain('What was done (optional)');
      expect(text()).toContain('Resolving does not change the attempt.');
      expect(buttons('Mark resolved')[0].disabled).toBe(false);

      press('Resolve…', 0);
      expect(root.querySelectorAll('textarea').length).toBe(1);
    });

    it('puts the form away without sending anything when it is cancelled, and gives focus back to its Resolve button', async () => {
      await open([row('r1')]);
      press('Resolve…');
      await settle();
      expect(document.activeElement).toBe(root.querySelector('textarea'));

      press('Cancel');
      await settle();

      expect(root.querySelector('textarea')).toBeNull();
      expect(document.activeElement).toBe(buttons('Resolve…')[0]);
      httpMock.expectNone(isResolve('r1'));
    });

    it('sends the note, turns the report into a line saying so in its own place, and does not read the queue again', async () => {
      await open([row('r1'), row('r2')]);
      press('Resolve…', 0);
      type(root.querySelector('textarea') as HTMLTextAreaElement, '  Added option C  ');

      press('Mark resolved');

      const request = httpMock.expectOne(isResolve('r1'));
      expect(request.request.body).toEqual({ note: '  Added option C  ' });
      request.flush(row('r1', { status: 'Resolved' }));
      fixture.detectChanges();

      httpMock.expectNone(isList);
      expect(results()).toHaveLength(1);
      expect(results()[0].textContent?.replace(/\s+/g, ' ').trim()).toBe(
        '✓ Marked the report from r1@example.com as resolved.',
      );
      expect(root.querySelector('.issue-result__note')?.textContent?.trim()).toBe(
        'What was done: Added option C',
      );
      expect(cards()[1].textContent).toContain('r2@example.com');
      expect(root.querySelector('.issue-queue__count')?.textContent?.trim()).toBe(
        '1 report is waiting.',
      );
    });

    it('moves focus to the line that says the report was resolved, so the next person to act is not lost at the top of the page', async () => {
      await open([row('r1'), row('r2')]);
      press('Resolve…', 0);
      press('Mark resolved');

      httpMock.expectOne(isResolve('r1')).flush(row('r1', { status: 'Resolved' }));
      await settle();

      expect(document.activeElement).toBe(results()[0]);
    });

    it('sends no note at all when none was written', async () => {
      await open([row('r1')]);
      press('Resolve…');

      press('Mark resolved');

      const request = httpMock.expectOne(isResolve('r1'));
      expect(request.request.body).toEqual({ note: null });
      request.flush(row('r1', { status: 'Resolved' }));
      fixture.detectChanges();

      expect(results()[0].textContent).toContain(
        'Marked the report from r1@example.com as resolved.',
      );
      expect(root.querySelector('.issue-result__note')).toBeNull();
      httpMock.expectNone(isList);
    });

    it('locks every button while it is on its way', async () => {
      await open([row('r1'), row('r2')]);
      press('Resolve…', 0);

      press('Mark resolved');

      expect(buttons('Mark resolved')[0].disabled).toBe(true);
      expect(buttons('Cancel')[0].disabled).toBe(true);
      expect(buttons('Resolve…')[0].disabled).toBe(true);
      httpMock.expectOne(isResolve('r1')).flush(row('r1', { status: 'Resolved' }));
      fixture.detectChanges();
      httpMock.expectNone(isList);
    });

    it('shows the API’s reason on the report that was refused, and leaves the form open', async () => {
      await open([row('r1'), row('r2')]);
      press('Resolve…', 0);
      press('Mark resolved');

      httpMock.expectOne(isResolve('r1')).flush(
        {
          title: 'issue_report_not_open',
          detail: 'This reported problem has already been resolved.',
        },
        { status: 409, statusText: 'Conflict' },
      );
      fixture.detectChanges();

      expect(cards()[0].querySelector('[role="alert"]')?.textContent).toContain(
        'already been resolved',
      );
      expect(cards()[1].querySelector('[role="alert"]')).toBeNull();
      expect(cards()[0].querySelector('textarea')).not.toBeNull();
      expect(buttons('Mark resolved')[0].disabled).toBe(false);
      expect(results()).toHaveLength(0);
    });

    it('keeps a resolved report in its place, with the reports after it still waiting in order', async () => {
      await open([row('r1'), row('r2'), row('r3')]);
      press('Resolve…', 1);
      press('Mark resolved');
      httpMock.expectOne(isResolve('r2')).flush(row('r2', { status: 'Resolved' }));
      fixture.detectChanges();

      const addresses = cards().map((card) => card.textContent?.match(/r\d@example\.com/)?.[0]);
      expect(addresses).toEqual(['r1@example.com', 'r2@example.com', 'r3@example.com']);
      expect(results()[0].textContent).toContain('r2@example.com');
      expect(root.querySelector('.issue-queue__count')?.textContent?.trim()).toBe(
        '2 reports are waiting.',
      );
    });
  });

  describe('in another language', () => {
    it('shows the title, the count and the resolved line in the language chosen', async () => {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, 'hi');
      await open([row('r1'), row('r2')]);

      expect(root.querySelector('h1')?.textContent).toBe(HI['admin.issues.title']);
      expect(root.querySelector('.issue-queue__count')?.textContent?.trim()).toBe(
        HI['admin.issues.waiting.other'].replace('{count}', '2'),
      );
    });

    it('shows a resolution in Marathi, with the note as it was written', async () => {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, 'mr');
      await open([row('r1')]);
      press(MR['admin.issues.resolve']);
      type(root.querySelector('textarea') as HTMLTextAreaElement, 'Added option C');
      press(MR['admin.issues.resolveSubmit']);
      httpMock.expectOne(isResolve('r1')).flush(row('r1', { status: 'Resolved' }));
      fixture.detectChanges();

      expect(results()[0].textContent?.replace(/\s+/g, ' ').trim()).toBe(
        `✓ ${MR['admin.issues.resolvedResult'].replace('{who}', 'r1@example.com')}`,
      );
      expect(root.querySelector('.issue-result__note')?.textContent?.trim()).toBe(
        MR['admin.issues.whatWasDone'].replace('{note}', 'Added option C'),
      );
    });
  });
});
