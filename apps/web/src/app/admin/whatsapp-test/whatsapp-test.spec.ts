import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, CanActivateFn, Router, RouterStateSnapshot } from '@angular/router';
import { afterEach, beforeEach, vi } from 'vitest';
import { routes } from '../../app.routes';
import { ADMIN_SECTIONS, Permission } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { WHATSAPP_MESSAGE_MAX_LENGTH, WHATSAPP_POLL_INTERVAL_MS, WHATSAPP_POLL_TIMEOUT_MS, WhatsAppTest } from './whatsapp-test';
import {
  WhatsAppDeliveryDto,
  WhatsAppFailureDto,
  WhatsAppFailureKind,
  WhatsAppSendResultDto,
  WhatsAppSettingDto,
  WhatsAppStatusDto,
  failureTitle,
} from './whatsapp-test.models';

const setting = (overrides: Partial<WhatsAppSettingDto> = {}): WhatsAppSettingDto => ({
  setting: 'WhatsApp__AccessToken',
  isSet: true,
  required: true,
  purpose: 'Lets the platform call the WhatsApp Cloud API.',
  value: null,
  ...overrides,
});

const status = (overrides: Partial<WhatsAppStatusDto> = {}): WhatsAppStatusDto => ({
  enabled: true,
  canSendMessages: true,
  canSendTemplate: true,
  canTrackDelivery: true,
  signInCodesUseWhatsApp: false,
  inviteCodesUseWhatsApp: false,
  settings: [setting()],
  problems: [],
  notes: [],
  ...overrides,
});

const failure = (overrides: Partial<WhatsAppFailureDto> = {}): WhatsAppFailureDto => ({
  kind: 'RecipientNotAllowed',
  explanation: 'The number is not on the list of recipients allowed while the app is in development.',
  metaCode: 131030,
  metaMessage: 'Recipient phone number not in allowed list',
  httpStatus: 400,
  ...overrides,
});

const sendResult = (overrides: Partial<WhatsAppSendResultDto> = {}): WhatsAppSendResultDto => ({
  sent: true,
  messageId: 'wamid.ABC',
  to: '********10',
  mode: 'Text',
  deliveryTracking: false,
  note: null,
  failure: null,
  ...overrides,
});

const delivery = (overrides: Partial<WhatsAppDeliveryDto> = {}): WhatsAppDeliveryDto => ({
  messageId: 'wamid.ABC',
  status: 'NotReported',
  recipient: '********10',
  updatedAtUtc: null,
  failure: null,
  ...overrides,
});

describe('WhatsAppTest', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<WhatsAppTest>;
  let root: HTMLElement;

  const isStatus = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/admin/whatsapp/status');
  const isSend = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/admin/whatsapp/messages');
  const isDelivery = (r: { method: string; url: string }) => r.method === 'GET' && r.url.includes('/v1/admin/whatsapp/messages/');

  beforeEach(async () => {
    vi.useFakeTimers();
    await TestBed.configureTestingModule({
      imports: [WhatsAppTest],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  /** Opens the page and answers the setup check request with `body`, or returns it unanswered when `body` is null. */
  function open(body: WhatsAppStatusDto | null = status()): TestRequest {
    fixture = TestBed.createComponent(WhatsAppTest);
    root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    const request = httpMock.expectOne(isStatus);
    if (body !== null) {
      request.flush(body);
      fixture.detectChanges();
    }
    return request;
  }

  /** The page's text with runs of whitespace collapsed, so template line breaks do not matter. */
  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ');
  const phoneInput = () => root.querySelector<HTMLInputElement>('#whatsapp-phone')!;
  const messageInput = () => root.querySelector<HTMLTextAreaElement>('#whatsapp-message');
  const radio = (value: string) => root.querySelector<HTMLInputElement>(`input[type="radio"][value="${value}"]`)!;
  const sendButton = () => root.querySelector<HTMLButtonElement>('form button[type="submit"]')!;
  const resultArea = () => root.querySelector<HTMLElement>('[aria-live="polite"]')!;
  const buttonLabelled = (label: string) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent?.trim() === label);

  function type(element: HTMLInputElement | HTMLTextAreaElement, value: string): void {
    element.value = value;
    element.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submit(): void {
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  /** Fills in a number and a message and sends, returning the request the page made. */
  function sendText(number = '919876543210', message = 'Hello'): TestRequest {
    type(phoneInput(), number);
    type(messageInput()!, message);
    submit();
    return httpMock.expectOne(isSend);
  }

  function sendTemplate(number = '919876543210'): TestRequest {
    type(phoneInput(), number);
    radio('SignInTemplate').click();
    fixture.detectChanges();
    submit();
    return httpMock.expectOne(isSend);
  }

  function respond(request: TestRequest, body: WhatsAppSendResultDto): void {
    request.flush(body);
    fixture.detectChanges();
  }

  /** Moves time on to the next poll and returns the delivery report request it made. */
  function nextPoll(): TestRequest {
    vi.advanceTimersByTime(WHATSAPP_POLL_INTERVAL_MS);
    return httpMock.expectOne(isDelivery);
  }

  function report(request: TestRequest, body: WhatsAppDeliveryDto): void {
    request.flush(body);
    fixture.detectChanges();
  }

  /** Sends a text message that WhatsApp accepts and reports on, leaving the page waiting for delivery reports. */
  function sendTracked(): void {
    respond(sendText(), sendResult({ deliveryTracking: true }));
  }

  const stepLabels = () => Array.from(root.querySelectorAll('.whatsapp-steps__step')).map((s) => s.textContent?.replace(/\s+/g, ' ').trim());
  const currentStep = () => root.querySelector('.whatsapp-steps li[aria-current="step"]')?.textContent?.replace(/\s+/g, ' ').trim();

  describe('the setup check', () => {
    it('opens with the heading and a sentence saying what the page is for', () => {
      open();

      expect(root.querySelector('h1')?.textContent).toBe('WhatsApp test');
      expect(text()).toContain("Send a message to a phone number through the platform's WhatsApp connection to check that it works. If it does not, this page says why.");
    });

    it('asks for the setup when the page opens, and says it is loading until it arrives', () => {
      const request = open(null);

      expect(request.request.method).toBe('GET');
      expect(text()).toContain('Loading…');
      expect(root.querySelector('table')).toBeNull();
    });

    it('says WhatsApp is on, in words as well as colour', () => {
      open(status({ enabled: true }));

      const pill = root.querySelector('.whatsapp-pill')!;
      expect(pill.textContent?.trim()).toBe('WhatsApp is on');
      expect(pill.classList).toContain('badge--active');
    });

    it('says WhatsApp is off, in words as well as colour', () => {
      open(status({ enabled: false }));

      const pill = root.querySelector('.whatsapp-pill')!;
      expect(pill.textContent?.trim()).toBe('WhatsApp is off');
      expect(pill.classList).toContain('badge--inactive');
      expect(pill.classList).not.toContain('badge--active');
    });

    it('lists every setting with its name, whether it is set, whether it is required and what it is for', () => {
      open(
        status({
          settings: [
            setting({ setting: 'WhatsApp__Enabled', isSet: true, required: true, purpose: 'The master switch.', value: 'true' }),
            setting({ setting: 'WhatsApp__AccessToken', isSet: false, required: true, purpose: 'Lets the platform call WhatsApp.', value: null }),
            setting({ setting: 'WhatsApp__SignInTemplateName', isSet: false, required: false, purpose: 'The sign-in code template.', value: null }),
          ],
        }),
      );

      const rows = Array.from(root.querySelectorAll('tbody tr'));
      expect(rows.length).toBe(3);
      const cells = (row: Element) => ({
        name: row.querySelector('code.whatsapp-setting')?.textContent?.trim(),
        badges: Array.from(row.querySelectorAll('.badge')).map((b) => b.textContent?.trim()),
        purpose: row.querySelectorAll('td')[2].textContent?.replace(/\s+/g, ' ').trim(),
      });
      expect(cells(rows[0])).toEqual({ name: 'WhatsApp__Enabled', badges: ['Set', 'required'], purpose: 'The master switch. Value: true' });
      expect(cells(rows[1])).toEqual({ name: 'WhatsApp__AccessToken', badges: ['Not set', 'required'], purpose: 'Lets the platform call WhatsApp.' });
      expect(cells(rows[2])).toEqual({ name: 'WhatsApp__SignInTemplateName', badges: ['Not set'], purpose: 'The sign-in code template.' });
    });

    it('styles a missing required setting as a problem, a set one as fine and a missing optional one as neutral', () => {
      open(
        status({
          settings: [
            setting({ setting: 'A', isSet: true, required: true }),
            setting({ setting: 'B', isSet: false, required: true }),
            setting({ setting: 'C', isSet: false, required: false }),
          ],
        }),
      );

      const badge = (row: number) => root.querySelectorAll('tbody tr')[row].querySelector('.badge')!;
      expect(badge(0).classList).toContain('badge--active');
      expect(badge(1).classList).toContain('badge--danger');
      expect(badge(2).classList).not.toContain('badge--active');
      expect(badge(2).classList).not.toContain('badge--danger');
    });

    it('shows a setting value only when the server sends one, and never for a secret', () => {
      open(
        status({
          settings: [
            setting({ setting: 'WhatsApp__SignInTemplateName', value: 'sign_in_code' }),
            setting({ setting: 'WhatsApp__AccessToken', value: null }),
          ],
        }),
      );

      const values = Array.from(root.querySelectorAll('.whatsapp-value')).map((v) => v.textContent?.replace(/\s+/g, ' ').trim());
      expect(values).toEqual(['Value: sign_in_code']);
    });

    it('labels the table for assistive technology, with a header for each column', () => {
      open();

      expect(root.querySelector('table caption')?.textContent?.trim()).toBe('WhatsApp settings');
      expect(Array.from(root.querySelectorAll('thead th')).map((h) => h.textContent?.trim())).toEqual(['Setting', 'Status', 'What it is for']);
    });

    it('lists the problems prominently, in the error style, and does not say nothing is missing', () => {
      open(status({ problems: ['The access token is not set.', 'The phone number id is not set.'] }));

      const box = root.querySelector('.whatsapp-problems')!;
      expect(box.classList).toContain('error-message');
      expect(box.querySelector('h3')?.textContent?.trim()).toBe('What is stopping WhatsApp from sending');
      expect(Array.from(box.querySelectorAll('li')).map((li) => li.textContent?.trim())).toEqual([
        'The access token is not set.',
        'The phone number id is not set.',
      ]);
      expect(text()).not.toContain('Nothing is missing.');
    });

    it('says nothing is missing, in the success style, when there are no problems', () => {
      open(status({ problems: [] }));

      const message = root.querySelector('.success-message')!;
      expect(message.textContent?.trim()).toBe('Nothing is missing.');
      expect(root.querySelector('.whatsapp-problems')).toBeNull();
    });

    it('lists the notes quietly, apart from the problems', () => {
      open(status({ problems: ['Not set up.'], notes: ['Delivery reports are not configured.'] }));

      const notes = root.querySelector('.whatsapp-notes')!;
      expect(notes.classList).toContain('hint');
      expect(notes.classList).not.toContain('error-message');
      expect(notes.textContent).toContain('Delivery reports are not configured.');
      expect(text()).toContain('Worth knowing');
    });

    it('leaves out the notes heading when there are no notes', () => {
      open(status({ notes: [] }));

      expect(root.querySelector('.whatsapp-notes')).toBeNull();
      expect(text()).not.toContain('Worth knowing');
    });

    it("shows the API's reason, with a Retry, when the setup check cannot be loaded", () => {
      open(null).flush({ title: 'forbidden', detail: 'No access.' }, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();

      expect(root.querySelector('[role="alert"]')?.textContent).toContain('No access.');
      expect(buttonLabelled('Retry')).toBeDefined();
      expect(root.querySelector('table')).toBeNull();
    });

    it('says the user is not allowed when a 403 explains nothing', () => {
      open(null).flush(null, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();

      expect(root.querySelector('[role="alert"]')?.textContent).toContain('You are not allowed to use the WhatsApp test.');
    });

    it('uses the usual message for any other failure', () => {
      open(null).flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(root.querySelector('[role="alert"]')?.textContent).toContain('Something went wrong. Please try again.');
    });

    it('tries again when Retry is pressed, and shows the setup once it loads', () => {
      open(null).flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      buttonLabelled('Retry')!.click();
      fixture.detectChanges();
      expect(root.querySelector('[role="alert"]')).toBeNull();
      httpMock.expectOne(isStatus).flush(status({ settings: [setting({ setting: 'WhatsApp__Enabled' })] }));
      fixture.detectChanges();

      expect(root.querySelector('[role="alert"]')).toBeNull();
      expect(text()).toContain('WhatsApp__Enabled');
      expect(buttonLabelled('Retry')).toBeUndefined();
    });

    it('reloads the setup when Refresh is pressed, and cannot be pressed again until it has loaded', () => {
      open(status({ enabled: false }));
      expect(text()).toContain('WhatsApp is off');

      buttonLabelled('Refresh')!.click();
      fixture.detectChanges();
      expect(buttonLabelled('Refresh')!.disabled).toBe(true);
      httpMock.expectOne(isStatus).flush(status({ enabled: true }));
      fixture.detectChanges();

      expect(text()).toContain('WhatsApp is on');
      expect(buttonLabelled('Refresh')!.disabled).toBe(false);
    });

    it('drops the old setup when a refresh fails, so stale facts are not shown beside the error', () => {
      open(status({ enabled: true }));

      buttonLabelled('Refresh')!.click();
      httpMock.expectOne(isStatus).flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(text()).not.toContain('WhatsApp is on');
      expect(root.querySelector('[role="alert"]')).not.toBeNull();
    });
  });

  describe('the form', () => {
    beforeEach(() => open());

    it('labels every control, and ties the hints to the controls they explain', () => {
      expect(root.querySelector('label[for="whatsapp-phone"]')?.textContent?.trim()).toBe('Phone number');
      expect(root.querySelector('label[for="whatsapp-message"]')?.textContent?.trim()).toBe('Message');
      const hintId = phoneInput().getAttribute('aria-describedby')!;
      expect(root.querySelector(`#${hintId}`)?.textContent).toContain(
        "With the country code, for example 919876543210. Without one, the platform's default country code is added.",
      );
      expect(root.querySelector('form')?.getAttribute('aria-labelledby')).toBe('whatsapp-send-heading');
      expect(root.querySelector('#whatsapp-send-heading')?.textContent).toBe('Send a message');
    });

    it('asks for a phone number the way a browser should: tel, required, no autofill', () => {
      expect(phoneInput().type).toBe('tel');
      expect(phoneInput().required).toBe(true);
      expect(phoneInput().getAttribute('autocomplete')).toBe('off');
    });

    it('offers the two things to send as a radio group, the message being the default', () => {
      expect(root.querySelector('fieldset legend')?.textContent?.trim()).toBe('What to send');
      const radios = Array.from(root.querySelectorAll<HTMLInputElement>('fieldset input[type="radio"]'));
      expect(radios.map((r) => r.value)).toEqual(['Text', 'SignInTemplate']);
      expect(new Set(radios.map((r) => r.name)).size).toBe(1);
      expect(radios.map((r) => r.checked)).toEqual([true, false]);
      const choices = Array.from(root.querySelectorAll('fieldset label')).map((l) => l.textContent?.replace(/\s+/g, ' ').trim());
      expect(choices).toEqual([
        'A message I write WhatsApp only delivers a free message to someone who messaged your WhatsApp number in the last 24 hours.',
        'The sign-in code template Works for anyone. Sends a test code that cannot be used to sign in.',
      ]);
    });

    it('cannot send until there is a number and a message', () => {
      expect(sendButton().disabled).toBe(true);

      type(phoneInput(), '919876543210');
      expect(sendButton().disabled).toBe(true);

      type(messageInput()!, 'Hello');
      expect(sendButton().disabled).toBe(false);
    });

    it('cannot send a blank number or a message of only spaces', () => {
      type(messageInput()!, 'Hello');
      type(phoneInput(), '   ');
      expect(sendButton().disabled).toBe(true);

      type(phoneInput(), '919876543210');
      type(messageInput()!, '   \n ');
      expect(sendButton().disabled).toBe(true);
    });

    it('sends nothing when submitted while the form is not valid', () => {
      submit();

      httpMock.expectNone(isSend);
    });

    it('needs only a number for the sign-in template, and hides the message box', () => {
      radio('SignInTemplate').click();
      fixture.detectChanges();
      expect(messageInput()).toBeNull();
      expect(sendButton().disabled).toBe(true);

      type(phoneInput(), '919876543210');

      expect(sendButton().disabled).toBe(false);
    });

    it('brings back the message that was typed when switching back to a message', () => {
      type(messageInput()!, 'Hello there');
      radio('SignInTemplate').click();
      fixture.detectChanges();
      radio('Text').click();
      fixture.detectChanges();

      expect(messageInput()!.value).toBe('Hello there');
    });

    it('counts the characters of the message against the limit, and stops the box at the limit', () => {
      expect(WHATSAPP_MESSAGE_MAX_LENGTH).toBe(1000);
      const counter = root.querySelector('.whatsapp-counter')!;
      expect(counter.querySelector('[aria-hidden="true"]')?.textContent).toBe('0 / 1000');

      type(messageInput()!, 'Hello');

      expect(counter.querySelector('[aria-hidden="true"]')?.textContent).toBe('5 / 1000');
      expect(counter.querySelector('.visually-hidden')?.textContent).toBe('5 of 1000 characters used');
      expect(messageInput()!.getAttribute('maxlength')).toBe('1000');
      expect(messageInput()!.getAttribute('aria-describedby')).toBe(counter.id);
    });

    it('sends the number and the message, both trimmed', () => {
      const request = sendText('  919876543210 ', '  Hello there \n');

      expect(request.request.body).toEqual({ phoneNumber: '919876543210', mode: 'Text', message: 'Hello there' });
      request.flush(sendResult());
    });

    it('sends the sign-in template without a message, even when one was typed', () => {
      type(messageInput()!, 'Typed, then changed my mind');
      const request = sendTemplate(' 919876543210 ');

      expect(request.request.body).toEqual({ phoneNumber: '919876543210', mode: 'SignInTemplate' });
      expect('message' in (request.request.body as object)).toBe(false);
      request.flush(sendResult({ mode: 'SignInTemplate' }));
    });

    it('says it is sending, and cannot be sent twice, until the answer arrives', () => {
      const request = sendText();

      expect(sendButton().textContent?.trim()).toBe('Sending…');
      expect(sendButton().disabled).toBe(true);
      submit();
      httpMock.expectNone(isSend);

      respond(request, sendResult());
      expect(sendButton().textContent?.trim()).toBe('Send to WhatsApp');
      expect(sendButton().disabled).toBe(false);
    });

    it('keeps what was typed after a send, so it can be sent again', () => {
      respond(sendText('919876543210', 'Hello'), sendResult());

      expect(phoneInput().value).toBe('919876543210');
      expect(messageInput()!.value).toBe('Hello');
    });
  });

  describe('a message WhatsApp accepted', () => {
    beforeEach(() => open());

    it('says it was handed to WhatsApp, to whom, with which id, and passes on the server note', () => {
      respond(sendText(), sendResult({ to: '********10', messageId: 'wamid.XYZ', note: 'Sent with the default country code.' }));

      expect(resultArea().querySelector('h2')?.textContent).toBe('Handed to WhatsApp');
      expect(resultArea().textContent).toContain('********10');
      expect(resultArea().querySelector('code')?.textContent).toBe('wamid.XYZ');
      expect(resultArea().textContent).toContain('Sent with the default country code.');
      expect(resultArea().querySelector('details')).toBeNull();
    });

    it('leaves out the recipient, id and note when the server sends none', () => {
      respond(sendText(), sendResult({ to: null, messageId: null, note: null }));

      expect(resultArea().querySelector('h2')?.textContent).toBe('Handed to WhatsApp');
      expect(resultArea().querySelector('strong')).toBeNull();
      expect(resultArea().querySelector('code')).toBeNull();
    });

    it('says delivery cannot be confirmed when delivery reports are not set up, and does not poll', () => {
      respond(sendText(), sendResult({ deliveryTracking: false }));

      expect(resultArea().textContent?.replace(/\s+/g, ' ')).toContain(
        'Delivery cannot be confirmed here because delivery reports are not set up; check the phone.',
      );
      expect(root.querySelector('.whatsapp-steps')).toBeNull();
      vi.advanceTimersByTime(WHATSAPP_POLL_TIMEOUT_MS * 2);
      httpMock.expectNone(isDelivery);
    });

    it('shows Sent, Delivered, Read with nothing marked until the first delivery report arrives', () => {
      sendTracked();

      expect(root.querySelector('.whatsapp-steps')?.getAttribute('aria-label')).toBe('Delivery progress');
      expect(stepLabels()).toEqual(['○ Sent', '○ Delivered', '○ Read']);
      expect(currentStep()).toBeUndefined();
      expect(text()).toContain('Waiting for a delivery report…');
    });

    it('asks for the delivery report every 3 seconds, one request at a time, for the message that was sent', () => {
      respond(sendText(), sendResult({ deliveryTracking: true, messageId: 'wamid.A/B=' }));

      vi.advanceTimersByTime(WHATSAPP_POLL_INTERVAL_MS - 1);
      httpMock.expectNone(isDelivery);

      vi.advanceTimersByTime(1);
      const first = httpMock.expectOne(isDelivery);
      expect(first.request.url).toContain('/v1/admin/whatsapp/messages/wamid.A%2FB%3D');

      // The first request is still waiting: the next tick must not pile a second one on top of it.
      vi.advanceTimersByTime(WHATSAPP_POLL_INTERVAL_MS);
      httpMock.expectNone(isDelivery);

      report(first, delivery({ status: 'NotReported' }));
      report(nextPoll(), delivery({ status: 'NotReported' }));
      expect(text()).toContain('Waiting for a delivery report…');
    });

    it('moves the highlighted step forward with each report, and stops asking once it is delivered', () => {
      sendTracked();

      report(nextPoll(), delivery({ status: 'sent' }));
      expect(stepLabels()).toEqual(['● Sent (current step)', '○ Delivered', '○ Read']);
      expect(currentStep()).toBe('● Sent (current step)');

      report(nextPoll(), delivery({ status: 'delivered' }));
      expect(stepLabels()).toEqual(['✓ Sent (done)', '● Delivered (current step)', '○ Read']);
      expect(currentStep()).toContain('Delivered');
      expect(text()).not.toContain('Waiting for a delivery report');

      vi.advanceTimersByTime(WHATSAPP_POLL_TIMEOUT_MS);
      httpMock.expectNone(isDelivery);
      expect(text()).not.toContain('No delivery report yet');
    });

    it('stops asking once it is read', () => {
      sendTracked();

      report(nextPoll(), delivery({ status: 'read' }));

      expect(stepLabels()).toEqual(['✓ Sent (done)', '✓ Delivered (done)', '● Read (current step)']);
      vi.advanceTimersByTime(WHATSAPP_POLL_TIMEOUT_MS);
      httpMock.expectNone(isDelivery);
    });

    it('gives up after 90 seconds with no report, and tells the administrator to check the phone', () => {
      sendTracked();
      const polls = WHATSAPP_POLL_TIMEOUT_MS / WHATSAPP_POLL_INTERVAL_MS;
      expect(polls).toBe(30);

      for (let i = 1; i < polls; i++) {
        report(nextPoll(), delivery({ status: 'NotReported' }));
      }
      expect(text()).not.toContain('No delivery report yet');
      expect(text()).toContain('Waiting for a delivery report…');

      report(nextPoll(), delivery({ status: 'NotReported' }));

      expect(text()).toContain('No delivery report yet. Check the phone.');
      expect(text()).not.toContain('Waiting for a delivery report');
      vi.advanceTimersByTime(WHATSAPP_POLL_TIMEOUT_MS);
      httpMock.expectNone(isDelivery);
    });

    it('says so when WhatsApp reported it as sent but never as delivered', () => {
      sendTracked();

      for (let i = 0; i < WHATSAPP_POLL_TIMEOUT_MS / WHATSAPP_POLL_INTERVAL_MS; i++) {
        report(nextPoll(), delivery({ status: 'sent' }));
      }

      expect(text()).toContain('WhatsApp reports the message as sent, but not yet as delivered. Check the phone.');
      expect(text()).not.toContain('No delivery report yet');
    });

    it('keeps asking when a delivery report cannot be fetched this time', () => {
      sendTracked();

      nextPoll().flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();
      expect(root.querySelector('.error-message')).toBeNull();
      report(nextPoll(), delivery({ status: 'delivered' }));

      expect(currentStep()).toContain('Delivered');
    });

    it('replaces the success with the failure when a delivery report says the message failed', () => {
      sendTracked();

      report(
        nextPoll(),
        delivery({
          status: 'failed',
          failure: failure({ kind: 'NotOnWhatsApp', explanation: 'That number has no WhatsApp account.', metaCode: 131026, httpStatus: null, metaMessage: 'Message undeliverable' }),
        }),
      );

      expect(resultArea().querySelector('h2')?.textContent).toBe('Not on WhatsApp');
      expect(text()).toContain('That number has no WhatsApp account.');
      expect(text()).toContain('WhatsApp accepted the message but could not deliver it.');
      expect(text()).not.toContain('Handed to WhatsApp');
      expect(root.querySelector('.whatsapp-steps')).toBeNull();
      expect(root.querySelector('.whatsapp-result--failed')).not.toBeNull();
      vi.advanceTimersByTime(WHATSAPP_POLL_TIMEOUT_MS);
      httpMock.expectNone(isDelivery);
    });

    it('still explains a failed delivery that arrives without a reason', () => {
      sendTracked();

      report(nextPoll(), delivery({ status: 'failed', failure: null }));

      expect(resultArea().querySelector('h2')?.textContent).toBe('Rejected by WhatsApp');
      expect(text()).toContain('WhatsApp did not deliver the message, and gave no reason.');
    });

    it('stops asking when the page is left', () => {
      sendTracked();
      const pending = nextPoll();

      fixture.destroy();

      expect(pending.cancelled).toBe(true);
      vi.advanceTimersByTime(WHATSAPP_POLL_TIMEOUT_MS);
      httpMock.expectNone(isDelivery);
    });

    it('stops asking when another message is sent, and follows the new one instead', () => {
      sendTracked();
      const stale = nextPoll();

      const second = sendText('919876543210', 'Again');
      expect(stale.cancelled).toBe(true);
      expect(resultArea().querySelector('.whatsapp-result')).toBeNull();
      respond(second, sendResult({ deliveryTracking: true, messageId: 'wamid.SECOND' }));

      const poll = nextPoll();
      expect(poll.request.url).toContain('wamid.SECOND');
      report(poll, delivery({ messageId: 'wamid.SECOND', status: 'delivered' }));
      expect(resultArea().querySelector('code')?.textContent).toBe('wamid.SECOND');
      vi.advanceTimersByTime(WHATSAPP_POLL_TIMEOUT_MS);
      httpMock.expectNone(isDelivery);
    });

    it('does not start asking when the page is left before the send is answered', () => {
      const request = sendText();

      fixture.destroy();

      expect(request.cancelled).toBe(true);
      vi.advanceTimersByTime(WHATSAPP_POLL_TIMEOUT_MS);
      httpMock.expectNone(isDelivery);
    });
  });

  describe('a message that could not be sent', () => {
    beforeEach(() => open());

    const failed = (why: Partial<WhatsAppFailureDto> = {}) => sendResult({ sent: false, messageId: null, to: null, failure: failure(why) });

    it('names the kind of failure in words, then says what is wrong', () => {
      respond(sendText(), failed({ kind: 'RecipientNotAllowed', explanation: 'Add the number to the allowed recipients in Meta.' }));

      expect(resultArea().querySelector('h2')?.textContent).toBe('Recipient not allowed');
      expect(resultArea().querySelector('.whatsapp-result p')?.textContent).toBe('Add the number to the allowed recipients in Meta.');
      expect(resultArea().textContent).not.toContain('Handed to WhatsApp');
      expect(root.querySelector('.whatsapp-result--failed')).not.toBeNull();
      expect(root.querySelector('.whatsapp-steps')).toBeNull();
      vi.advanceTimersByTime(WHATSAPP_POLL_TIMEOUT_MS);
      httpMock.expectNone(isDelivery);
    });

    it('keeps what Meta said folded away until it is asked for, and lists all of it', () => {
      respond(sendText(), failed({ metaCode: 131030, httpStatus: 400, metaMessage: 'Recipient phone number not in allowed list' }));

      const details = resultArea().querySelector<HTMLDetailsElement>('details')!;
      expect(details.open).toBe(false);
      expect(details.querySelector('summary')?.textContent?.trim()).toBe('What Meta said');
      expect(Array.from(details.querySelectorAll('dt')).map((t) => t.textContent)).toEqual(['Meta code', 'HTTP status', 'Meta message']);
      expect(Array.from(details.querySelectorAll('dd')).map((d) => d.textContent)).toEqual(['131030', '400', 'Recipient phone number not in allowed list']);
    });

    it('lists only the details Meta gave', () => {
      respond(sendText(), failed({ metaCode: null, httpStatus: 503, metaMessage: null }));

      const details = resultArea().querySelector('details')!;
      expect(Array.from(details.querySelectorAll('dt')).map((t) => t.textContent)).toEqual(['HTTP status']);
      expect(Array.from(details.querySelectorAll('dd')).map((d) => d.textContent)).toEqual(['503']);
    });

    it('shows a Meta code of zero, which is a code like any other', () => {
      respond(sendText(), failed({ metaCode: 0, httpStatus: null, metaMessage: null }));

      expect(Array.from(resultArea().querySelectorAll('dd')).map((d) => d.textContent)).toEqual(['0']);
    });

    it('has no Meta section when WhatsApp was never reached', () => {
      respond(sendText(), failed({ kind: 'SwitchedOff', explanation: 'WhatsApp is switched off.', metaCode: null, httpStatus: null, metaMessage: null }));

      expect(resultArea().querySelector('h2')?.textContent).toBe('WhatsApp is switched off');
      expect(resultArea().querySelector('details')).toBeNull();
    });

    it('explains a failure the server reported without a reason', () => {
      respond(sendText(), sendResult({ sent: false, failure: null }));

      expect(resultArea().querySelector('h2')?.textContent).toBe('Rejected by WhatsApp');
      expect(text()).toContain('WhatsApp did not deliver the message, and gave no reason.');
    });

    it('treats an unreachable number as a failure to show, not as an error in the request', () => {
      respond(sendText('12', 'Hello'), failed({ kind: 'InvalidNumber', explanation: 'That is not a phone number WhatsApp can reach.', metaCode: null, httpStatus: null, metaMessage: null }));

      expect(resultArea().querySelector('h2')?.textContent).toBe('Invalid phone number');
      expect(resultArea().querySelector('.error-message')).toBeNull();
    });

    it('clears the failure when the next message is sent, and shows the new outcome', () => {
      respond(sendText(), failed());

      const second = sendText();
      expect(resultArea().querySelector('.whatsapp-result')).toBeNull();
      respond(second, sendResult());

      expect(resultArea().querySelector('h2')?.textContent).toBe('Handed to WhatsApp');
    });
  });

  describe('a request that failed', () => {
    beforeEach(() => open());

    const bad = (body: object | null, statusCode: number) => {
      const request = sendText();
      request.flush(body, { status: statusCode, statusText: 'Error' });
      fixture.detectChanges();
    };

    it("shows the reason for a request the API says is malformed, and lets the form be used again", () => {
      bad({ title: 'invalid_whatsapp_message', detail: 'The message must be at most 1000 characters.' }, 400);

      expect(resultArea().querySelector('.error-message')?.textContent).toBe('The message must be at most 1000 characters.');
      expect(resultArea().querySelector('.whatsapp-result')).toBeNull();
      expect(sendButton().disabled).toBe(false);
    });

    it('still says something useful when a malformed request comes back with no detail', () => {
      bad({ title: 'invalid_whatsapp_message' }, 400);

      expect(resultArea().querySelector('.error-message')?.textContent).toBe('Check the phone number and the message, then try again.');
    });

    it("shows the API's reason for any other HTTP error", () => {
      bad({ title: 'server_error', detail: 'The server could not take the message.' }, 500);

      expect(resultArea().querySelector('.error-message')?.textContent).toBe('The server could not take the message.');
    });

    it('uses the usual message when the error carries no reason', () => {
      bad(null, 500);

      expect(resultArea().querySelector('.error-message')?.textContent).toBe('Something went wrong. Please try again.');
    });

    it('says the user is not allowed, in the usual words, on a 403 with no reason', () => {
      bad(null, 403);

      expect(resultArea().querySelector('.error-message')?.textContent).toBe('You are not allowed to use the WhatsApp test.');
    });

    it('clears the error when the next message is sent', () => {
      bad({ title: 'invalid_whatsapp_message', detail: 'The message is empty.' }, 400);

      const second = sendText();
      expect(resultArea().querySelector('.error-message')).toBeNull();
      respond(second, sendResult());

      expect(resultArea().querySelector('.error-message')).toBeNull();
    });
  });

  it('announces its results politely, from a region that is on the page before there is anything to announce', () => {
    open();

    expect(resultArea()).not.toBeNull();
    expect(resultArea().textContent?.trim()).toBe('');
    expect(resultArea().getAttribute('aria-live')).toBe('polite');
  });
});

describe('failureTitle', () => {
  const kinds: WhatsAppFailureKind[] = [
    'SwitchedOff',
    'NotConfigured',
    'InvalidNumber',
    'InvalidToken',
    'PermissionDenied',
    'WrongPhoneNumberId',
    'WrongApiAddress',
    'InvalidRequest',
    'InvalidRecipient',
    'TemplateNotFound',
    'TemplateUnavailable',
    'TemplateMismatch',
    'RecipientNotAllowed',
    'ReEngagementRequired',
    'NotOnWhatsApp',
    'NumberNotRegistered',
    'PaymentProblem',
    'AccountRestricted',
    'RateLimited',
    'ServiceUnavailable',
    'Unreachable',
    'TimedOut',
    'Rejected',
  ];

  it('gives every kind its own readable heading', () => {
    const titles = kinds.map(failureTitle);

    expect(new Set(titles).size).toBe(kinds.length);
    for (const title of titles) {
      expect(title).toMatch(/^[A-Z]/);
    }
  });

  it.each([
    ['RecipientNotAllowed', 'Recipient not allowed'],
    ['NotOnWhatsApp', 'Not on WhatsApp'],
    ['ReEngagementRequired', 'Re-engagement required'],
    ['WrongPhoneNumberId', 'Wrong phone number ID'],
    ['WrongApiAddress', 'Wrong WhatsApp address'],
    ['InvalidRequest', 'Invalid request'],
    ['InvalidRecipient', 'Invalid recipient'],
  ])('writes %s as "%s"', (kind, title) => {
    expect(failureTitle(kind)).toBe(title);
  });

  it('makes a readable heading from a kind this page has not heard of', () => {
    expect(failureTitle('SomethingNewHappened')).toBe('Something new happened');
    expect(failureTitle('quota_exceeded')).toBe('Quota exceeded');
    expect(failureTitle('')).toBe('Not sent');
  });
});

describe('WhatsApp test route and navigation', () => {
  const route = routes.find((r) => r.path === 'admin/whatsapp')!;
  const state = { url: '/admin/whatsapp' } as unknown as RouterStateSnapshot;

  function runGuard(isAuthenticated: boolean, permissions: string[]) {
    const createUrlTree = vi.fn((commands: unknown[]) => `tree:${String(commands[0])}`);
    TestBed.configureTestingModule({
      providers: [
        {
          provide: AuthSessionService,
          useValue: { isAuthenticated: () => isAuthenticated, hasPermission: (code: string) => permissions.includes(code) },
        },
        { provide: Router, useValue: { createUrlTree } },
      ],
    });
    const guard = route.canActivate![0] as CanActivateFn;
    return TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, state));
  }

  it('has a route that opens this page', async () => {
    expect(route).toBeDefined();
    expect(await route.loadComponent!()).toBe(WhatsAppTest);
  });

  it('lets a user with the WhatsApp test permission in', () => {
    expect(runGuard(true, ['admin.whatsapp.test'])).toBe(true);
  });

  it('sends a signed-in user without the permission to /forbidden, whatever else they may do', () => {
    expect(runGuard(true, ['identity.otp.read', 'exam.manage', 'question.manage'])).toBe('tree:/forbidden');
  });

  it('sends a signed-out visitor to /login', () => {
    expect(runGuard(false, [])).toBe('tree:/login');
  });

  it('is the one permission the API defines for the WhatsApp test', () => {
    expect(Permission.WhatsAppTest).toBe('admin.whatsapp.test');
  });

  it('is offered in the admin areas under the same permission and address as its route', () => {
    const section = ADMIN_SECTIONS.find((s) => s.label === 'WhatsApp test')!;

    expect(section).toBeDefined();
    expect(section.permission).toBe(Permission.WhatsAppTest);
    expect(section.path).toBe('/admin/whatsapp');
    expect(section.path).toBe(`/${route.path}`);
    expect(section.icon).toBe('whatsapp');
    expect(section.description).toBe('Check that WhatsApp can send, and see exactly why not if it cannot.');
  });
});
