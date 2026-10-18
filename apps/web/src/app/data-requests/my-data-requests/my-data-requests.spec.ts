import { formatDate } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { I18nService, LANGUAGE_STORAGE_KEY } from '../../i18n/i18n.service';
import { HI } from '../../i18n/messages.hi';
import { DataRequestDto } from '../data-request.models';
import { MyDataRequests } from './my-data-requests';

/**
 * The candidate's data requests (FR-48).
 *
 * The app runs without zone.js, so a page is redrawn only for what a signal tells it about. These tests therefore wait for the page to
 * settle by itself after each answer (`whenStable`) and never force a redraw with `detectChanges`: a forced redraw would show the new
 * state even for a page that a browser leaves on the old one.
 */

const request = (id: string, overrides: Partial<DataRequestDto> = {}): DataRequestDto => ({
  id,
  userId: 'candidate-1',
  kind: 'Access',
  status: 'Received',
  details: null,
  receivedAtUtc: '2026-10-10T09:00:00Z',
  dueAtUtc: '2026-11-09T09:00:00Z',
  isOverdue: false,
  resolvedAtUtc: null,
  resolutionNote: null,
  ...overrides,
});

/** A date as the page writes it, in this machine's time zone like the page's own date pipe, with the spaces the tests read text by. */
const when = (iso: string): string => formatDate(iso, 'medium', 'en-US').replace(/\s+/g, ' ');

const COPY = 'Send me a copy of my data';
const CORRECT = 'Correct something that is wrong';
const ERASE = 'Erase my data';

describe('MyDataRequests', () => {
  let fixture: ComponentFixture<MyDataRequests>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;

  const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/me/data-requests');
  const isRaise = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/me/data-requests');

  /** Draws the page, with its read of the requests still in flight. */
  async function create(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [MyDataRequests],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    await TestBed.inject(I18nService).ready();
    httpMock = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(MyDataRequests);
    root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  }

  /** Draws the page and answers its read, as the API would, then lets the page settle. */
  async function open(answer: DataRequestDto[] | 'error' = []): Promise<void> {
    await create();
    const read = httpMock.expectOne(isList);
    if (answer === 'error') {
      read.flush({ title: 'forbidden', detail: 'You cannot see these requests.' }, { status: 403, statusText: 'Forbidden' });
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
  const text = () => line(root);
  const cards = () => Array.from(root.querySelectorAll<HTMLElement>('ul.request-list > li'));
  /** The small headings inside a card (what was written, the staff note), and the text under each, in order. */
  const labels = (card: HTMLElement) => Array.from(card.querySelectorAll('.request__label')).map((element) => line(element));
  const texts = (card: HTMLElement) => Array.from(card.querySelectorAll('.request__text')).map((element) => line(element));
  /** The radio button of the choice whose words contain this text. */
  const radio = (label: string) =>
    Array.from(root.querySelectorAll<HTMLLabelElement>('label.inline-check'))
      .find((candidate) => line(candidate).includes(label))
      ?.querySelector('input') as HTMLInputElement;
  const detailsBox = () => root.querySelector('textarea') as HTMLTextAreaElement;
  const sendButton = () => root.querySelector('button[type="submit"]') as HTMLButtonElement;

  async function choose(label: string): Promise<void> {
    radio(label).click();
    await fixture.whenStable();
  }

  async function write(value: string): Promise<void> {
    detailsBox().value = value;
    detailsBox().dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  async function send(): Promise<void> {
    sendButton().click();
    await fixture.whenStable();
  }

  describe('the list of requests', () => {
    it('says it is loading while the requests are being read, and no longer once they arrive', async () => {
      await create();

      expect(text()).toContain('Loading…');

      httpMock.expectOne(isList).flush([]);
      await fixture.whenStable();

      expect(text()).not.toContain('Loading…');
    });

    it('shows the newest request first, however the API ordered them, each with what it asks for and where it stands', async () => {
      await open([
        request('old', { kind: 'Access', status: 'Completed', receivedAtUtc: '2026-09-01T09:00:00Z', resolvedAtUtc: '2026-09-05T10:00:00Z' }),
        request('new', { kind: 'Erasure' }),
      ]);

      expect(cards().map((card) => line(card.querySelector('h3')))).toEqual([
        'Erasure of the data Waiting for an answer',
        'Copy of the data Done',
      ]);
    });

    it('says when a waiting request was sent and when staff must answer it', async () => {
      await open([request('r1')]);

      expect(line(cards()[0])).toContain(`Sent ${when('2026-10-10T09:00:00Z')}`);
      expect(line(cards()[0])).toContain(`Staff answer it by ${when('2026-11-09T09:00:00Z')}`);
      expect(cards()[0].querySelector('.warning-message')).toBeNull();
    });

    it('says plainly that the answer is late once the API marks the request overdue', async () => {
      await open([request('r1', { isOverdue: true })]);

      expect(line(cards()[0].querySelector('.warning-message'))).toBe(
        `The answer was due ${when('2026-11-09T09:00:00Z')} and has not come yet.`,
      );
      expect(line(cards()[0])).not.toContain('Staff answer it by');
    });

    it('shows what staff wrote and when they answered a completed request', async () => {
      await open([
        request('r1', {
          kind: 'Access',
          status: 'Completed',
          resolvedAtUtc: '2026-10-12T10:30:00Z',
          resolutionNote: 'A copy was e-mailed to you.',
        }),
      ]);

      const content = line(cards()[0]);
      expect(content).toContain(`Answered ${when('2026-10-12T10:30:00Z')}`);
      expect(labels(cards()[0])).toEqual(['Staff note']);
      expect(texts(cards()[0])).toEqual(['A copy was e-mailed to you.']);
      expect(content).not.toContain('Staff answer it by');
    });

    it('shows a refusal as not done, with the reason staff gave', async () => {
      await open([
        request('r1', {
          kind: 'Erasure',
          status: 'Rejected',
          resolvedAtUtc: '2026-10-12T10:30:00Z',
          resolutionNote: 'Exam results must be kept for three years.',
        }),
      ]);

      expect(line(cards()[0].querySelector('h3'))).toBe('Erasure of the data Not done');
      expect(labels(cards()[0])).toEqual(['Staff note']);
      expect(texts(cards()[0])).toEqual(['Exam results must be kept for three years.']);
    });

    it('says staff left no note when an answered request has none', async () => {
      await open([request('r1', { status: 'Completed', resolvedAtUtc: '2026-10-12T10:30:00Z', resolutionNote: null })]);

      expect(labels(cards()[0])).toEqual(['Staff note']);
      expect(texts(cards()[0])).toEqual(['Staff did not leave a note.']);
    });

    it('shows what the candidate wrote as text, never as markup', async () => {
      await open([request('r1', { kind: 'Correction', details: '<b>My</b> name is\nspelt wrong' })]);

      const written = cards()[0].querySelector('.request__text') as HTMLElement;
      expect(written.textContent).toBe('<b>My</b> name is\nspelt wrong');
      expect(written.querySelector('b')).toBeNull();
    });

    it('says there are no requests yet when the answer is empty', async () => {
      await open([]);

      expect(root.querySelector('.empty-state')?.textContent).toContain('You have not sent a request yet.');
      expect(text()).not.toContain('Loading…');
    });

    it('shows why when the requests cannot be read, no longer says it is loading, and keeps the form', async () => {
      await open('error');

      expect(root.querySelector('.error-message')?.textContent).toContain('You cannot see these requests.');
      expect(text()).not.toContain('Loading…');
      expect(radio(COPY)).not.toBeNull();
    });
  });

  describe('sending a request', () => {
    it('offers the three kinds, each with a line saying what it leads to, and sends nothing until one is chosen', async () => {
      await open([]);

      expect(radio(COPY)).not.toBeNull();
      expect(radio(CORRECT)).not.toBeNull();
      expect(radio(ERASE)).not.toBeNull();
      expect(line(radio(COPY).closest('label'))).toContain('Staff send you a copy of the personal information');
      expect(line(radio(ERASE).closest('label'))).toContain('as far as the law and the rules on how long records are kept allow');

      await send();

      httpMock.expectNone(isRaise);
      expect(root.querySelector('#data-request-kind-error')?.textContent).toContain('Choose what you want.');
      expect(root.querySelector('fieldset')?.getAttribute('aria-describedby')).toBe('data-request-kind-error');
    });

    it('sends the chosen kind with the details trimmed', async () => {
      await open([]);

      await choose(CORRECT);
      await write('  My date of birth is wrong.  ');
      await send();

      const post = httpMock.expectOne(isRaise);
      expect(post.request.body).toEqual({ kind: 'Correction', details: 'My date of birth is wrong.' });
      post.flush(request('r1', { kind: 'Correction' }));
      await fixture.whenStable();
    });

    it('sends no details at all when the box is blank or only spaces', async () => {
      await open([]);

      await choose(COPY);
      await write('   ');
      await send();

      const post = httpMock.expectOne(isRaise);
      expect(post.request.body).toEqual({ kind: 'Access', details: null });
      post.flush(request('r1'));
      await fixture.whenStable();
    });

    it('limits the details to what the API takes', async () => {
      await open([]);

      expect(detailsBox().maxLength).toBe(1000);
      expect(line(root.querySelector('#data-request-details-hint'))).toContain('At most 1000 characters.');
    });

    it('says the request was sent and when it is due, moves focus to that line, and puts the request at the top of the list', async () => {
      await open([request('earlier', { kind: 'Access', status: 'Completed', receivedAtUtc: '2026-09-01T09:00:00Z', resolvedAtUtc: '2026-09-02T09:00:00Z' })]);

      await choose(ERASE);
      await send();
      httpMock.expectOne(isRaise).flush(request('r2', { kind: 'Erasure', receivedAtUtc: '2026-10-17T08:00:00Z', dueAtUtc: '2026-11-16T08:00:00Z' }));
      await fixture.whenStable();

      const sent = root.querySelector('.success-message') as HTMLElement;
      expect(line(sent)).toBe(`Your request was sent. Staff must answer it by ${when('2026-11-16T08:00:00Z')}.`);
      expect(document.activeElement).toBe(sent);
      expect(cards().map((card) => line(card.querySelector('h3')))).toEqual([
        'Erasure of the data Waiting for an answer',
        'Copy of the data Done',
      ]);
    });

    it('empties the form once the request is sent, and the kind just asked for can no longer be chosen', async () => {
      await open([]);

      await choose(ERASE);
      await write('Please remove my phone number.');
      await send();
      httpMock.expectOne(isRaise).flush(request('r1', { kind: 'Erasure' }));
      await fixture.whenStable();

      expect(radio(ERASE).checked).toBe(false);
      expect(radio(ERASE).disabled).toBe(true);
      expect(radio(COPY).disabled).toBe(false);
      expect(detailsBox().value).toBe('');
      expect(root.querySelector('#data-request-kind-error')).toBeNull();
    });

    it('locks the form while the request is on its way, and a second submit meanwhile sends nothing more', async () => {
      await open([]);
      await choose(COPY);

      await send();
      // Not disabled, so a keyboard user who pressed it keeps the focus; it says it is busy, and a second press does nothing.
      expect(sendButton().disabled).toBe(false);
      expect(sendButton().getAttribute('aria-disabled')).toBe('true');
      expect(line(sendButton())).toBe('Sending…');
      expect(radio(COPY).disabled).toBe(true);
      expect(detailsBox().disabled).toBe(true);
      (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
      await fixture.whenStable();

      const post = httpMock.expectOne(isRaise);
      post.flush(request('r1'));
      await fixture.whenStable();

      expect(sendButton().getAttribute('aria-disabled')).toBeNull();
      expect(radio(COPY).disabled).toBe(true);
    });

    it('keeps the focus on the Send button when a request fails, so a keyboard user is not sent back to the top of the page', async () => {
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      await open([]);
      await choose(COPY);
      sendButton().focus();

      await send();
      expect(document.activeElement).toBe(sendButton());
      httpMock.expectOne(isRaise).flush('boom', { status: 500, statusText: 'Server Error' });
      await fixture.whenStable();

      expect(root.querySelector('.error-message')).not.toBeNull();
      expect(document.activeElement).toBe(sendButton());
      consoleError.mockRestore();
    });

    it('takes the old confirmation away once the person starts another request, so it never reads as the answer to the new one', async () => {
      await open([]);
      await choose(COPY);
      await send();
      httpMock.expectOne(isRaise).flush(request('r1'));
      await fixture.whenStable();
      expect(root.querySelector('.success-message')).not.toBeNull();

      await choose(ERASE);
      expect(root.querySelector('.success-message')).toBeNull();

      await send();
      httpMock.expectOne(isRaise).flush({ title: 'invalid_data_request', detail: 'Nope.' }, { status: 400, statusText: 'Bad Request' });
      await fixture.whenStable();
      expect(root.querySelector('.error-message')?.textContent).toContain('Nope.');

      await write('typing again');
      expect(root.querySelector('.error-message')).toBeNull();
    });

    it('shows the API’s reason when a request is refused, keeps what was typed, and lets the person try again', async () => {
      await open([]);
      await choose(CORRECT);
      await write('Wrong date of birth.');

      await send();
      httpMock.expectOne(isRaise).flush({ title: 'invalid_data_request', detail: 'The details can be at most 1000 characters.' }, { status: 400, statusText: 'Bad Request' });
      await fixture.whenStable();

      expect(root.querySelector('.error-message')?.textContent).toContain('The details can be at most 1000 characters.');
      expect(radio(CORRECT).checked).toBe(true);
      expect(detailsBox().value).toBe('Wrong date of birth.');
      expect(sendButton().disabled).toBe(false);
      expect(root.querySelector('.success-message')).toBeNull();
    });

    it('says something went wrong when the failure has no reason of its own', async () => {
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      await open([]);
      await choose(COPY);

      await send();
      httpMock.expectOne(isRaise).flush('boom', { status: 500, statusText: 'Server Error' });
      await fixture.whenStable();

      expect(root.querySelector('.error-message')?.textContent).toContain('Something went wrong. Please try again.');
      consoleError.mockRestore();
    });

    it('reads the list again when the API says a request of this kind is open already, so the choice shows as taken', async () => {
      await open([]);
      await choose(ERASE);

      await send();
      httpMock.expectOne(isRaise).flush(
        { title: 'data_request_already_open', detail: 'You already have an open request of this kind. It will be answered before you can send another.' },
        { status: 409, statusText: 'Conflict' },
      );
      await fixture.whenStable();

      expect(root.querySelector('.error-message')?.textContent).toContain('You already have an open request of this kind.');
      httpMock.expectOne(isList).flush([request('open-erasure', { kind: 'Erasure' })]);
      await fixture.whenStable();

      expect(radio(ERASE).disabled).toBe(true);
      expect(radio(ERASE).checked).toBe(false);
      expect(cards()).toHaveLength(1);
    });
  });

  describe('kinds that already have an open request', () => {
    it('cannot be chosen again, and say why, while the other kinds still can', async () => {
      await open([request('r1', { kind: 'Correction' })]);

      expect(radio(CORRECT).disabled).toBe(true);
      expect(line(radio(CORRECT).closest('label'))).toContain('You already have an open request of this kind.');
      expect(radio(COPY).disabled).toBe(false);
      expect(radio(ERASE).disabled).toBe(false);
      expect(line(radio(COPY).closest('label'))).not.toContain('You already have an open request of this kind.');
    });

    it('can be asked for again once the request was answered', async () => {
      await open([request('r1', { kind: 'Access', status: 'Completed', resolvedAtUtc: '2026-10-12T10:30:00Z' })]);

      expect(radio(COPY).disabled).toBe(false);
    });

    it('leave nothing to fill in when every kind has one, and say so', async () => {
      await open([request('a', { kind: 'Access' }), request('c', { kind: 'Correction' }), request('e', { kind: 'Erasure' })]);

      expect(root.querySelectorAll('input[type="radio"]')).toHaveLength(0);
      expect(root.querySelector('textarea')).toBeNull();
      expect(sendButton()).toBeNull();
      expect(root.querySelector('.info-message')?.textContent).toContain('You have an open request of every kind.');
      expect(cards()).toHaveLength(3);
    });
  });

  describe('in another language', () => {
    it('shows the page in Hindi when Hindi is chosen, including the words for where a request stands', async () => {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, 'hi');
      await open([request('r1', { kind: 'Erasure' })]);

      expect(root.querySelector('h1')?.textContent).toBe(HI['dataRequests.mine.title']);
      expect(line(cards()[0].querySelector('h3'))).toBe(`${HI['dataRequests.kind.Erasure']} ${HI['dataRequests.status.Received']}`);
      expect(line(radio(HI['dataRequests.mine.kind.Access']).closest('label'))).toContain(HI['dataRequests.mine.help.Access']);
    });
  });
});
