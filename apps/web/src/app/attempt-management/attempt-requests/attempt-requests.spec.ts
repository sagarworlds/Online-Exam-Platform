import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { I18nService } from '../../i18n/i18n.service';
import { HI } from '../../i18n/messages.hi';
import { MR } from '../../i18n/messages.mr';
import { AttemptRequestRow } from '../attempt-admin.models';
import { AttemptRequests } from './attempt-requests';

const row = (id: string, overrides: Partial<AttemptRequestRow> = {}): AttemptRequestRow => ({
  id,
  examId: 'e1',
  examName: 'Maths Final',
  candidateId: `c-${id}`,
  candidateEmail: `${id}@example.com`,
  message: 'Power cut',
  requestedAtUtc: '2026-10-05T05:00:00Z',
  status: 'Pending',
  decidedAtUtc: null,
  decisionNote: null,
  ...overrides,
});

describe('AttemptRequests', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<AttemptRequests>;
  let root: HTMLElement;

  const isList = (r: { method: string; url: string }) =>
    r.method === 'GET' && r.url.includes('/v1/attempt-requests');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AttemptRequests],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  function open(rows: AttemptRequestRow[] | 'error') {
    fixture = TestBed.createComponent(AttemptRequests);
    fixture.detectChanges();
    const list = httpMock.expectOne(isList);
    expect(list.request.params.get('status')).toBe('pending');
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

  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ').trim();
  const buttons = (label: string) =>
    Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === label);
  const press = (label: string, index = 0) => {
    buttons(label)[index].click();
    fixture.detectChanges();
  };
  const resultLines = () => Array.from(root.querySelectorAll<HTMLElement>('.request__result'));
  const reasonBox = () => root.querySelector<HTMLTextAreaElement>('textarea')!;

  /** Lets the page draw what the last action changed, and any focus that waits for it, to be moved. */
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('lists the waiting requests with who asked, for which exam, and why', () => {
    open([row('amy'), row('ben', { message: null, examName: null })]);

    expect(text()).toContain('amy@example.com');
    expect(text()).toContain('Maths Final');
    expect(text()).toContain('Power cut');
    expect(text()).toContain('They gave no reason.');
    expect(text()).toContain('An exam that can no longer be read');
    expect(text()).toContain('asked');
  });

  it('says how many requests are waiting, in the singular for one and the plural for more', () => {
    open([row('amy'), row('ben')]);
    expect(root.querySelector('.request-queue__count')?.textContent?.trim()).toBe(
      '2 requests are waiting.',
    );

    open([row('amy')]);
    expect(root.querySelector('.request-queue__count')?.textContent?.trim()).toBe(
      '1 request is waiting.',
    );
  });

  it('says when nobody is waiting, and shows no request', () => {
    open([]);

    expect(text()).toContain('No candidates are waiting for another attempt.');
    expect(root.querySelector('.request')).toBeNull();
  });

  it('shows the reason when the queue cannot be read', () => {
    open('error');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('No access.');
  });

  it('turns an approved request into a line saying so, in its own place, and leaves the others waiting', () => {
    open([row('amy'), row('ben'), row('cara')]);

    press('Give another attempt', 0);
    httpMock
      .expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/attempt-requests/amy/approve'))
      .flush(row('amy', { status: 'Approved', candidateNotified: true }));
    fixture.detectChanges();

    expect(resultLines().length).toBe(1);
    expect(resultLines()[0].textContent?.replace(/\s+/g, ' ').trim()).toBe(
      '✓ Gave amy@example.com another attempt. They were e-mailed.',
    );
    expect(root.querySelector('.request')?.classList).toContain('request--given');
    expect(root.querySelector('.request')?.textContent).toContain('Gave amy@example.com');
    expect(buttons('Give another attempt').length).toBe(2);
    expect(root.querySelector('.request-queue__count')?.textContent?.trim()).toBe(
      '2 requests are waiting.',
    );
  });

  it('moves focus to the line that says what was decided, so the next action is not lost at the top of the page', async () => {
    open([row('amy'), row('ben')]);

    press('Give another attempt', 0);
    httpMock
      .expectOne((r) => r.method === 'POST' && r.url.endsWith('/amy/approve'))
      .flush(row('amy', { status: 'Approved' }));
    await settle();

    expect(document.activeElement).toBe(resultLines()[0]);
  });

  it('keeps the request and shows the API’s reason on it when approving is refused', () => {
    open([row('amy')]);

    press('Give another attempt');
    httpMock
      .expectOne((r) => r.method === 'POST' && r.url.endsWith('/amy/approve'))
      .flush(
        {
          title: 'exam_closed',
          detail: 'The exam is closed, so another attempt could never be used.',
        },
        { status: 409, statusText: 'Conflict' },
      );
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('could never be used');
    expect(resultLines().length).toBe(0);
    expect(buttons('Give another attempt').length).toBe(1);
  });

  it('declines with the typed reason, which the candidate will see, and says so on the line that replaces it', () => {
    open([row('amy')]);
    press('Decline…');
    reasonBox().value = '  Speak to your teacher  ';
    reasonBox().dispatchEvent(new Event('input'));
    fixture.detectChanges();

    press('Decline request');

    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/amy/decline'));
    expect(post.request.body).toEqual({ note: 'Speak to your teacher' });
    post.flush(row('amy', { status: 'Declined', candidateNotified: false }));
    fixture.detectChanges();

    expect(resultLines()[0].textContent?.replace(/\s+/g, ' ').trim()).toBe(
      '✗ Declined the request from amy@example.com. They could not be e-mailed, so let them know yourself.',
    );
    expect(root.querySelector('.request')?.classList).toContain('request--declined');
    expect(text()).toContain('No requests are waiting now.');
  });

  it('declines with no note when none was typed, and Cancel sends nothing', () => {
    open([row('amy')]);
    press('Decline…');
    press('Cancel');
    httpMock.expectNone((r) => r.method === 'POST');

    press('Decline…');
    press('Decline request');

    expect(
      httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/amy/decline')).request.body,
    ).toEqual({ note: null });
  });

  it('says the candidate sees the reason, and ties that hint to the box it describes', () => {
    open([row('amy')]);
    press('Decline…');

    const hintId = reasonBox().getAttribute('aria-describedby')!;
    expect(root.querySelector(`#${hintId}`)?.textContent?.trim()).toBe(
      'The candidate sees this reason with the decision.',
    );
    expect(root.querySelector('label[for="' + reasonBox().id + '"]')?.textContent?.trim()).toBe(
      'Reason to show the candidate (optional)',
    );
  });

  it('moves focus into the reason box when the form opens, and back to its Decline button when cancelled', async () => {
    open([row('amy')]);

    press('Decline…');
    await settle();
    expect(document.activeElement).toBe(reasonBox());

    press('Cancel');
    await settle();
    expect(document.activeElement).toBe(buttons('Decline…')[0]);
  });

  it('works on one request at a time', () => {
    open([row('amy'), row('ben')]);

    press('Give another attempt', 0);

    expect(buttons('Give another attempt').every((b) => b.disabled)).toBe(true);
    httpMock
      .expectOne((r) => r.method === 'POST' && r.url.endsWith('/amy/approve'))
      .flush(row('amy', { status: 'Approved' }));
  });

  it('never shows "null" for a candidate who is no longer enrolled, in the heading or the form label', () => {
    open([row('amy', { candidateEmail: null })]);

    expect(root.querySelector('h3')?.textContent?.trim()).toBe(
      'A candidate who is no longer enrolled',
    );
    press('Decline…');
    expect(root.querySelector('form')?.getAttribute('aria-label')).toBe(
      'Decline the request from the candidate',
    );
    expect(text()).not.toContain('null');
  });

  describe('in another language', () => {
    it('shows the page in the language chosen, with the decision line in that language too', async () => {
      await TestBed.inject(I18nService).setLanguage('mr');
      open([row('amy')]);

      expect(root.querySelector('h1')?.textContent).toBe(MR['admin.requests.title']);
      press(MR['admin.requests.give']);
      httpMock
        .expectOne((r) => r.method === 'POST' && r.url.endsWith('/amy/approve'))
        .flush(row('amy', { status: 'Approved', candidateNotified: true }));
      fixture.detectChanges();

      expect(resultLines()[0].textContent?.replace(/\s+/g, ' ').trim()).toBe(
        `✓ ${MR['admin.requests.givenResult'].replace('{who}', 'amy@example.com')} ${MR['admin.requests.emailed']}`,
      );
    });

    it('shows the title and the count in Hindi, the count in the plural when more than one waits', async () => {
      await TestBed.inject(I18nService).setLanguage('hi');
      open([row('amy'), row('ben')]);

      expect(root.querySelector('h1')?.textContent).toBe(HI['admin.requests.title']);
      expect(root.querySelector('.request-queue__count')?.textContent?.trim()).toBe(
        HI['admin.requests.waiting.other'].replace('{count}', '2'),
      );
    });
  });
});
