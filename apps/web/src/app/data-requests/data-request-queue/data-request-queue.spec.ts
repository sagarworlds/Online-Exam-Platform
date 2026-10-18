import { formatDate } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { I18nService, LANGUAGE_STORAGE_KEY } from '../../i18n/i18n.service';
import { HI } from '../../i18n/messages.hi';
import { OpenDataRequestDto } from '../data-request.models';
import { DataRequestQueue } from './data-request-queue';

/**
 * Staff's queue of data requests (FR-48).
 *
 * The app runs without zone.js, so a page is redrawn only for what a signal tells it about. These tests therefore wait for the page to
 * settle by itself after each answer (`whenStable`) and never force a redraw with `detectChanges`: a forced redraw would show the new
 * state even for a page that a browser leaves on the old one.
 */

const waiting = (id: string, overrides: Partial<OpenDataRequestDto> = {}): OpenDataRequestDto => ({
  id,
  userId: `user-${id}`,
  kind: 'Access',
  status: 'Received',
  details: 'Please send me a copy.',
  receivedAtUtc: '2026-10-01T09:00:00Z',
  dueAtUtc: '2026-10-31T09:00:00Z',
  isOverdue: false,
  resolvedAtUtc: null,
  resolutionNote: null,
  requesterName: 'Asha Rao',
  requesterEmail: `${id}@example.com`,
  requesterPhoneNumber: '+919800000001',
  ...overrides,
});

/** A date as the page writes it, in this machine's time zone like the page's own date pipe, with the spaces the tests read text by. */
const when = (iso: string): string => formatDate(iso, 'medium', 'en-US').replace(/\s+/g, ' ');

describe('DataRequestQueue', () => {
  let fixture: ComponentFixture<DataRequestQueue>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;

  const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/data-requests/open');
  const isResolve = (id: string) => (r: { method: string; url: string }) =>
    r.method === 'POST' && r.url.endsWith(`/v1/data-requests/${id}/resolve`);

  /** Draws the page, with its read of the queue still in flight. */
  async function create(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [DataRequestQueue],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    await TestBed.inject(I18nService).ready();
    httpMock = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(DataRequestQueue);
    root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  }

  /** Draws the page and answers its read, as the API would, then lets the page settle. */
  async function open(answer: OpenDataRequestDto[] | 'error' = []): Promise<void> {
    await create();
    const read = httpMock.expectOne(isList);
    if (answer === 'error') {
      read.flush({ title: 'forbidden', detail: 'You cannot answer data requests.' }, { status: 403, statusText: 'Forbidden' });
    } else {
      read.flush(answer);
    }
    await fixture.whenStable();
  }

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  const line = (element: Element | null | undefined) => (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const cards = () => Array.from(root.querySelectorAll<HTMLElement>('ul.request-list > li'));
  const button = (scope: ParentNode, label: string) =>
    Array.from(scope.querySelectorAll('button')).find((candidate) => line(candidate) === label) as HTMLButtonElement;
  const noteBox = (card: HTMLElement) => card.querySelector('textarea') as HTMLTextAreaElement;
  const contact = (card: HTMLElement) => Array.from(card.querySelectorAll('.request__contact li')).map((item) => line(item));

  async function press(scope: ParentNode, label: string): Promise<void> {
    button(scope, label).click();
    await fixture.whenStable();
  }

  async function type(box: HTMLTextAreaElement, value: string): Promise<void> {
    box.value = value;
    box.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  describe('the queue', () => {
    it('says it is loading while the queue is being read, and no longer once it arrives', async () => {
      await create();

      expect(line(root)).toContain('Loading…');

      httpMock.expectOne(isList).flush([]);
      await fixture.whenStable();

      expect(line(root)).not.toContain('Loading…');
    });

    it('shows each request in the order the API gave, with what it asks for, who asked, when it came and when it is due', async () => {
      await open([
        waiting('a1', { kind: 'Erasure', requesterName: 'Asha Rao', requesterEmail: 'asha@example.com', requesterPhoneNumber: '+919800000001' }),
        waiting('b2', { kind: 'Correction', requesterName: 'Ben Das', requesterEmail: 'ben@example.com', requesterPhoneNumber: null }),
      ]);

      expect(cards().map((card) => line(card.querySelector('h3')))).toEqual(['Erasure of the data', 'Correction of the data']);
      expect(contact(cards()[0])).toEqual(['Asha Rao', 'asha@example.com', '+919800000001']);
      expect(contact(cards()[1])).toEqual(['Ben Das', 'ben@example.com']);
      expect(line(cards()[0].querySelector('.card__meta'))).toBe(
        `Received ${when('2026-10-01T09:00:00Z')} · Due ${when('2026-10-31T09:00:00Z')}`,
      );
      expect(line(cards()[0])).toContain('Please send me a copy.');
    });

    it('counts the requests that are waiting, in the singular and the plural', async () => {
      await open([waiting('a1')]);
      expect(line(root.querySelector('.page > p.card__meta'))).toBe('1 request is waiting for an answer');

      TestBed.resetTestingModule();
      await open([waiting('a1'), waiting('b2'), waiting('c3')]);
      expect(line(root.querySelector('.page > p.card__meta'))).toBe('3 requests are waiting for an answer');
    });

    it('marks an overdue request in words, and no other', async () => {
      await open([waiting('late', { isOverdue: true }), waiting('fine')]);

      expect(line(cards()[0].querySelector('.badge'))).toContain('Overdue');
      expect(cards()[1].querySelector('.badge')).toBeNull();
    });

    it('names the account when the API gives no name, address or number for it, and says so', async () => {
      await open([waiting('a1', { requesterName: null, requesterEmail: null, requesterPhoneNumber: null })]);

      expect(contact(cards()[0])).toEqual([]);
      expect(line(cards()[0])).toContain('Account user-a1');
      expect(line(cards()[0])).toContain('This account has no e-mail address or phone number on file.');
    });

    it('says there is no address or number even when the account has a name, since a name alone cannot be written to', async () => {
      await open([
        waiting('a1', { requesterName: 'Asha Rao', requesterEmail: null, requesterPhoneNumber: null }),
        waiting('b2', { requesterName: 'Ben Das', requesterEmail: 'ben@example.com', requesterPhoneNumber: null }),
        waiting('c3', { requesterName: 'Chitra', requesterEmail: null, requesterPhoneNumber: '+919800000003' }),
      ]);

      expect(contact(cards()[0])).toEqual(['Asha Rao']);
      expect(line(cards()[0])).toContain('This account has no e-mail address or phone number on file.');
      expect(line(cards()[1])).not.toContain('no e-mail address or phone number');
      expect(line(cards()[2])).not.toContain('no e-mail address or phone number');
    });

    it('names only the account, and does not claim the account has no contact, when the API does not report who asked at all', async () => {
      const bare = waiting('a1');
      delete bare.requesterName;
      delete bare.requesterEmail;
      delete bare.requesterPhoneNumber;
      await open([bare]);

      expect(line(cards()[0])).toContain('Account user-a1');
      expect(line(cards()[0])).not.toContain('no e-mail address or phone number');
    });

    it('says when a request came with no details', async () => {
      await open([waiting('a1', { details: null })]);

      expect(line(cards()[0])).toContain('They gave no details.');
    });

    it('shows what the person wrote, and who they are, as text and never as markup', async () => {
      await open([waiting('a1', { details: '<img src=x onerror=alert(1)>\nSecond line', requesterName: '<b>Asha</b>' })]);

      expect(cards()[0].querySelector('img')).toBeNull();
      expect(cards()[0].querySelector('b')).toBeNull();
      expect(cards()[0].querySelector('.request__text')?.textContent).toBe('<img src=x onerror=alert(1)>\nSecond line');
      expect(contact(cards()[0])[0]).toBe('<b>Asha</b>');
    });

    it('says no request is waiting when the queue is empty, with no count', async () => {
      await open([]);

      expect(root.querySelector('.empty-state')?.textContent).toContain('No requests are waiting for an answer.');
      expect(root.querySelector('.page > p.card__meta')).toBeNull();
      expect(line(root)).not.toContain('Loading…');
    });

    it('shows why when the queue cannot be read, and no longer says it is loading', async () => {
      await open('error');

      expect(root.querySelector('.error-message')?.textContent).toContain('You cannot answer data requests.');
      expect(line(root)).not.toContain('Loading…');
    });
  });

  describe('answering a request', () => {
    it('offers two answers on each request, and opens a form for the one chosen, naming who it is about, with focus in its note box', async () => {
      await open([waiting('a1'), waiting('b2')]);

      expect(button(cards()[0], 'Record as done…')).toBeDefined();
      expect(button(cards()[0], 'Refuse…')).toBeDefined();

      await press(cards()[0], 'Record as done…');

      const form = cards()[0].querySelector('form') as HTMLFormElement;
      expect(form.getAttribute('aria-label')).toBe('Record the request of a1@example.com as done');
      expect(line(form.querySelector('label'))).toBe('What was done (optional)');
      expect(form.querySelector('label')?.getAttribute('for')).toBe(noteBox(cards()[0]).id);
      expect(noteBox(cards()[0]).maxLength).toBe(1000);
      expect(document.activeElement).toBe(noteBox(cards()[0]));
      expect(cards()[1].querySelector('form')).toBeNull();
    });

    it('records a request as done with no note, and sends nothing but the outcome', async () => {
      await open([waiting('a1')]);

      await press(cards()[0], 'Record as done…');
      await press(cards()[0], 'Record as done');

      const post = httpMock.expectOne(isResolve('a1'));
      expect(post.request.body).toEqual({ outcome: 'Completed', note: null });
      post.flush({ ...waiting('a1'), status: 'Completed' });
      await fixture.whenStable();
    });

    it('sends the note trimmed when one is given', async () => {
      await open([waiting('a1')]);

      await press(cards()[0], 'Record as done…');
      await type(noteBox(cards()[0]), '  Copy e-mailed on 17 October.  ');
      await press(cards()[0], 'Record as done');

      const post = httpMock.expectOne(isResolve('a1'));
      expect(post.request.body).toEqual({ outcome: 'Completed', note: 'Copy e-mailed on 17 October.' });
      post.flush({ ...waiting('a1'), status: 'Completed' });
      await fixture.whenStable();
    });

    it('turns the request into a line saying what was recorded, in its place, moves focus to it, and does not read the queue again', async () => {
      await open([waiting('a1', { kind: 'Access' }), waiting('b2', { kind: 'Correction' }), waiting('c3', { kind: 'Erasure' })]);

      await press(cards()[1], 'Record as done…');
      await press(cards()[1], 'Record as done');
      httpMock.expectOne(isResolve('b2')).flush({ ...waiting('b2', { kind: 'Correction' }), status: 'Completed' });
      await fixture.whenStable();

      expect(cards()).toHaveLength(3);
      const result = cards()[1].querySelector('.request-result') as HTMLElement;
      expect(line(result)).toBe('✓ Recorded as done: Correction of the data, b2@example.com.');
      expect(result.classList.contains('request-result--refused')).toBe(false);
      expect(document.activeElement).toBe(result);
      expect(cards()[1].querySelector('form')).toBeNull();
      expect(cards()[0].querySelector('.request-result')).toBeNull();
      expect(cards()[2].querySelector('.request-result')).toBeNull();
      expect(line(root.querySelector('.page > p.card__meta'))).toBe('2 requests are waiting for an answer');
      httpMock.expectNone(isList);
    });

    it('needs a reason to refuse: with none, or only spaces, it says so, keeps focus in the box, and sends nothing', async () => {
      await open([waiting('a1')]);

      await press(cards()[0], 'Refuse…');
      expect(line(cards()[0].querySelector('label'))).toBe('Why it is refused (required)');
      expect(noteBox(cards()[0]).getAttribute('aria-required')).toBe('true');

      await type(noteBox(cards()[0]), '    ');
      await press(cards()[0], 'Refuse request');

      httpMock.expectNone(isResolve('a1'));
      expect(line(cards()[0].querySelector('.field-error'))).toBe('Say why it is refused. The person is owed a reason.');
      expect(noteBox(cards()[0]).getAttribute('aria-invalid')).toBe('true');
      expect(noteBox(cards()[0]).getAttribute('aria-describedby')).toContain('note-error-a1');
      expect(document.activeElement).toBe(noteBox(cards()[0]));

      await type(noteBox(cards()[0]), 'E');
      expect(cards()[0].querySelector('.field-error')).toBeNull();
      expect(noteBox(cards()[0]).getAttribute('aria-invalid')).toBeNull();
    });

    it('refuses with the reason sent trimmed, and turns the request into a line saying it was refused', async () => {
      await open([waiting('a1', { kind: 'Erasure' })]);

      await press(cards()[0], 'Refuse…');
      await type(noteBox(cards()[0]), '  Exam results must be kept for three years.  ');
      await press(cards()[0], 'Refuse request');

      const post = httpMock.expectOne(isResolve('a1'));
      expect(post.request.body).toEqual({ outcome: 'Rejected', note: 'Exam results must be kept for three years.' });
      post.flush({ ...waiting('a1', { kind: 'Erasure' }), status: 'Rejected' });
      await fixture.whenStable();

      const result = cards()[0].querySelector('.request-result') as HTMLElement;
      expect(line(result)).toBe('✗ Refused: Erasure of the data, a1@example.com.');
      expect(result.classList.contains('request-result--refused')).toBe(true);
      expect(document.activeElement).toBe(result);
    });

    it('names the person by address, then name, then number, then account, in the line it leaves', async () => {
      await open([
        waiting('a1', { requesterEmail: null }),
        waiting('b2', { requesterEmail: null, requesterName: null }),
        waiting('c3', { requesterEmail: null, requesterName: null, requesterPhoneNumber: null }),
      ]);

      for (const [index, id] of ['a1', 'b2', 'c3'].entries()) {
        await press(cards()[index], 'Record as done…');
        await press(cards()[index], 'Record as done');
        httpMock.expectOne(isResolve(id)).flush({ ...waiting(id), status: 'Completed' });
        await fixture.whenStable();
      }

      expect(cards().map((card) => line(card.querySelector('.request-result')))).toEqual([
        '✓ Recorded as done: Copy of the data, Asha Rao.',
        '✓ Recorded as done: Copy of the data, +919800000001.',
        '✓ Recorded as done: Copy of the data, Account user-c3.',
      ]);
    });

    it('puts the form away on Cancel, and gives focus back to the button that opened it', async () => {
      await open([waiting('a1')]);

      await press(cards()[0], 'Refuse…');
      await press(cards()[0], 'Cancel');

      expect(cards()[0].querySelector('form')).toBeNull();
      expect(document.activeElement).toBe(button(cards()[0], 'Refuse…'));

      await press(cards()[0], 'Record as done…');
      await press(cards()[0], 'Cancel');

      expect(document.activeElement).toBe(button(cards()[0], 'Record as done…'));
      httpMock.expectNone((r) => r.method === 'POST');
    });

    it('keeps one form open at a time, so a stray click cannot answer two requests', async () => {
      await open([waiting('a1'), waiting('b2')]);

      await press(cards()[0], 'Record as done…');
      await type(noteBox(cards()[0]), 'half written');
      await press(cards()[1], 'Refuse…');

      expect(cards()[0].querySelector('form')).toBeNull();
      expect(cards()[1].querySelector('form')).not.toBeNull();
      expect(noteBox(cards()[1]).value).toBe('');
    });

    it('locks every answer while one is being recorded, and the one being recorded says so', async () => {
      await open([waiting('a1'), waiting('b2')]);

      await press(cards()[0], 'Record as done…');
      await press(cards()[0], 'Record as done');

      // The button that was pressed stays enabled, so the focus on it is not lost; it says it is busy, and a second press does nothing.
      expect(button(cards()[0], 'Saving…').disabled).toBe(false);
      expect(button(cards()[0], 'Saving…').getAttribute('aria-disabled')).toBe('true');
      expect(button(cards()[0], 'Cancel').disabled).toBe(true);
      expect(noteBox(cards()[0]).disabled).toBe(true);
      expect(button(cards()[1], 'Record as done…').disabled).toBe(true);
      expect(button(cards()[1], 'Refuse…').disabled).toBe(true);

      // A second submit while the first is on its way sends nothing more.
      (cards()[0].querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
      await fixture.whenStable();

      const post = httpMock.expectOne(isResolve('a1'));
      post.flush({ ...waiting('a1'), status: 'Completed' });
      await fixture.whenStable();

      expect(button(cards()[1], 'Record as done…').disabled).toBe(false);
    });

    it('turns a request someone else answered first into a line saying so, in its place, with focus on it, and counts it as no longer waiting', async () => {
      await open([waiting('a1'), waiting('b2')]);
      await press(cards()[0], 'Record as done…');
      await type(noteBox(cards()[0]), 'Done.');

      await press(cards()[0], 'Record as done');
      httpMock
        .expectOne(isResolve('a1'))
        .flush({ title: 'data_request_not_open', detail: 'This request has already been answered.' }, { status: 409, statusText: 'Conflict' });
      await fixture.whenStable();

      const result = cards()[0].querySelector('.request-result') as HTMLElement;
      expect(line(result)).toBe('– No longer waiting, so someone else may have answered it: Copy of the data, a1@example.com.');
      expect(result.classList.contains('request-result--elsewhere')).toBe(true);
      expect(document.activeElement).toBe(result);
      expect(cards()[0].querySelector('form')).toBeNull();
      expect(cards()[0].querySelector('.error-message')).toBeNull();
      expect(line(root.querySelector('.page > p.card__meta'))).toBe('1 request is waiting for an answer');
      expect(cards()[1].querySelector('.request-result')).toBeNull();
    });

    it('does the same when the request is gone altogether', async () => {
      await open([waiting('a1')]);
      await press(cards()[0], 'Refuse…');
      await type(noteBox(cards()[0]), 'No.');

      await press(cards()[0], 'Refuse request');
      httpMock
        .expectOne(isResolve('a1'))
        .flush({ title: 'data_request_not_found', detail: 'No data request has that id.' }, { status: 404, statusText: 'Not Found' });
      await fixture.whenStable();

      expect(line(cards()[0].querySelector('.request-result'))).toContain('No longer waiting');
    });

    it('keeps the focus on the button that was pressed when an answer fails, so a keyboard user is not sent back to the top of the page', async () => {
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      await open([waiting('a1')]);
      await press(cards()[0], 'Record as done…');
      const submit = button(cards()[0], 'Record as done');
      submit.focus();

      submit.click();
      await fixture.whenStable();
      httpMock.expectOne(isResolve('a1')).flush('boom', { status: 500, statusText: 'Server Error' });
      await fixture.whenStable();

      expect(cards()[0].querySelector('.error-message')).not.toBeNull();
      expect(document.activeElement).toBe(button(cards()[0], 'Record as done'));
      consoleError.mockRestore();
    });

    it('shows the API’s reason on that request when it is refused, keeps the form so the person can try again or cancel, and leaves the others alone', async () => {
      await open([waiting('a1'), waiting('b2')]);
      await press(cards()[0], 'Record as done…');
      await type(noteBox(cards()[0]), 'Done.');

      await press(cards()[0], 'Record as done');
      httpMock
        .expectOne(isResolve('a1'))
        .flush({ title: 'invalid_data_request', detail: 'The note can be at most 1000 characters.' }, { status: 400, statusText: 'Bad Request' });
      await fixture.whenStable();

      expect(cards()[0].querySelector('.error-message')?.textContent).toContain('The note can be at most 1000 characters.');
      expect(noteBox(cards()[0]).value).toBe('Done.');
      expect(button(cards()[0], 'Record as done').disabled).toBe(false);
      expect(cards()[1].querySelector('.error-message')).toBeNull();
      expect(cards()[0].querySelector('.request-result')).toBeNull();
    });

    it('says something went wrong when the failure has no reason of its own, and clears it on the next try', async () => {
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      await open([waiting('a1')]);
      await press(cards()[0], 'Record as done…');

      await press(cards()[0], 'Record as done');
      httpMock.expectOne(isResolve('a1')).flush('boom', { status: 500, statusText: 'Server Error' });
      await fixture.whenStable();
      expect(cards()[0].querySelector('.error-message')?.textContent).toContain('Something went wrong. Please try again.');

      await press(cards()[0], 'Record as done');
      expect(cards()[0].querySelector('.error-message')).toBeNull();
      httpMock.expectOne(isResolve('a1')).flush({ ...waiting('a1'), status: 'Completed' });
      await fixture.whenStable();
      consoleError.mockRestore();
    });
  });

  describe('in another language', () => {
    it('shows the page, and the line an answer leaves, in Hindi when Hindi is chosen', async () => {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, 'hi');
      await open([waiting('a1', { kind: 'Access' })]);

      expect(root.querySelector('h1')?.textContent).toBe(HI['admin.dataRequests.title']);
      expect(line(cards()[0].querySelector('h3'))).toBe(HI['dataRequests.kind.Access']);

      await press(cards()[0], HI['admin.dataRequests.completeOpen']);
      await press(cards()[0], HI['admin.dataRequests.completeSubmit']);
      httpMock.expectOne(isResolve('a1')).flush({ ...waiting('a1'), status: 'Completed' });
      await fixture.whenStable();

      expect(line(cards()[0].querySelector('.request-result'))).toBe(
        `✓ ${HI['admin.dataRequests.completedResult'].replace('{kind}', HI['dataRequests.kind.Access']).replace('{who}', 'a1@example.com')}`,
      );
    });
  });
});
