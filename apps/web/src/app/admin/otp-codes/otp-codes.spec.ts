import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, vi } from 'vitest';
import { ClipboardService } from '../../shared/clipboard/clipboard.service';
import { ToastService } from '../../shared/toast/toast.service';
import { OutstandingOtp } from './otp-codes.models';
import { OtpCodes, REVEAL_MS } from './otp-codes';

const code = (overrides: Partial<OutstandingOtp> = {}): OutstandingOtp => ({
  challengeId: 'c1',
  purpose: 'Login',
  channel: 'Email',
  destination: 'amy@example.com',
  code: '123456',
  expiresAtUtc: '2026-10-05T05:10:00Z',
  ...overrides,
});

describe('OtpCodes', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<OtpCodes>;
  let root: HTMLElement;

  const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.includes('/v1/admin/otp-codes');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OtpCodes],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(OtpCodes);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  function search(text: string): void {
    const input = root.querySelector('input')!;
    input.value = text;
    input.dispatchEvent(new Event('input'));
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  const buttonsLabelled = (label: string) =>
    Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === label) as HTMLButtonElement[];

  it('shows no code, and looks nothing up, until a search is made', () => {
    httpMock.expectNone(isList);
    expect(root.querySelector('.otp-code')).toBeNull();
  });

  it('asks for an email or phone number when the search is empty, and looks nothing up', () => {
    search('   ');

    httpMock.expectNone(isList);
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Enter an email or phone number to find its codes.');
    expect(root.querySelector('input')!.getAttribute('aria-invalid')).toBe('true');
  });

  it('searches by the text typed, and says for each code what it is for and how it was sent', () => {
    search('  amy  ');

    const request = httpMock.expectOne(isList);
    expect(request.request.params.get('destination')).toBe('amy');
    request.flush([code(), code({ challengeId: 'c2', destination: '+15550100', channel: 'Sms', purpose: 'Registration', code: '654321' })]);
    fixture.detectChanges();

    const text = root.textContent ?? '';
    expect(text).toContain('amy@example.com');
    expect(text).toContain('Sign-in code, sent by email, expires at');
    expect(text).toContain('Registration code, sent by SMS, expires at');
    expect(buttonsLabelled('Show code')).toHaveLength(2);
  });

  it('keeps every code hidden until it is asked for', () => {
    search('amy');
    httpMock.expectOne(isList).flush([code()]);
    fixture.detectChanges();

    expect(root.querySelector('.otp-code')).toBeNull();
    expect(root.textContent).not.toContain('123456');
  });

  it('shows a code in two groups of three digits when asked, and hides it again on request', () => {
    search('amy');
    httpMock.expectOne(isList).flush([code()]);
    fixture.detectChanges();

    buttonsLabelled('Show code')[0].click();
    fixture.detectChanges();

    expect(root.querySelector('.otp-code')?.textContent?.trim()).toBe('123 456');
    expect(buttonsLabelled('Show code')).toHaveLength(0);
    expect(buttonsLabelled('Hide code')[0].getAttribute('aria-expanded')).toBe('true');

    buttonsLabelled('Hide code')[0].click();
    fixture.detectChanges();

    expect(root.querySelector('.otp-code')).toBeNull();
  });

  it('hides a shown code again after a minute', () => {
    vi.useFakeTimers();
    search('amy');
    httpMock.expectOne(isList).flush([code()]);
    fixture.detectChanges();
    buttonsLabelled('Show code')[0].click();
    fixture.detectChanges();

    vi.advanceTimersByTime(REVEAL_MS - 1);
    fixture.detectChanges();
    expect(root.querySelector('.otp-code')).not.toBeNull();

    vi.advanceTimersByTime(1);
    fixture.detectChanges();
    expect(root.querySelector('.otp-code')).toBeNull();
  });

  it('says so, in a message a screen reader hears, when a code hides by itself', () => {
    vi.useFakeTimers();
    search('amy');
    httpMock.expectOne(isList).flush([code()]);
    fixture.detectChanges();
    buttonsLabelled('Show code')[0].click();
    fixture.detectChanges();
    const toast = TestBed.inject(ToastService);
    expect(toast.toasts()).toEqual([]);

    vi.advanceTimersByTime(REVEAL_MS);

    expect(toast.toasts().map((t) => [t.kind, t.message])).toEqual([['info', 'The code for amy@example.com was hidden. Show it again if you still need it.']]);
  });

  it('does not say a code was hidden when the person hid it', () => {
    vi.useFakeTimers();
    search('amy');
    httpMock.expectOne(isList).flush([code()]);
    fixture.detectChanges();
    buttonsLabelled('Show code')[0].click();
    fixture.detectChanges();

    buttonsLabelled('Hide code')[0].click();
    vi.advanceTimersByTime(REVEAL_MS);

    expect(TestBed.inject(ToastService).toasts()).toEqual([]);
  });

  describe('copying a code', () => {
    function reveal(): void {
      search('amy');
      httpMock.expectOne(isList).flush([code()]);
      fixture.detectChanges();
      buttonsLabelled('Show code')[0].click();
      fixture.detectChanges();
    }

    it('is offered beside Hide once a code is shown, and not before', () => {
      search('amy');
      httpMock.expectOne(isList).flush([code()]);
      fixture.detectChanges();
      expect(buttonsLabelled('Copy code')).toHaveLength(0);

      buttonsLabelled('Show code')[0].click();
      fixture.detectChanges();

      expect(buttonsLabelled('Copy code')).toHaveLength(1);
    });

    it('puts the six digits on the clipboard, without the space the screen shows, and says so', async () => {
      const copy = vi.spyOn(TestBed.inject(ClipboardService), 'copy').mockResolvedValue(true);
      reveal();

      buttonsLabelled('Copy code')[0].click();
      await fixture.whenStable();

      expect(copy).toHaveBeenCalledWith('123456');
      expect(TestBed.inject(ToastService).toasts().map((t) => [t.kind, t.message])).toEqual([['success', 'The code is copied.']]);
    });

    it('says the browser refused, and leaves the code on screen to be selected', async () => {
      vi.spyOn(TestBed.inject(ClipboardService), 'copy').mockResolvedValue(false);
      reveal();

      buttonsLabelled('Copy code')[0].click();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(TestBed.inject(ToastService).toasts().map((t) => t.kind)).toEqual(['error']);
      expect(root.querySelector('.otp-code')?.textContent?.trim()).toBe('123 456');
    });
  });

  describe('focus', () => {
    function show(): void {
      search('amy');
      httpMock.expectOne(isList).flush([code()]);
      fixture.detectChanges();
      document.body.appendChild(root);
    }

    afterEach(() => root.remove());

    it('goes to Copy when Show code was pressed, since that button is gone', async () => {
      show();
      const showButton = buttonsLabelled('Show code')[0];
      showButton.focus();

      showButton.click();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(buttonsLabelled('Copy code')[0]);
    });

    it('goes to Show code when Hide code was pressed', async () => {
      show();
      buttonsLabelled('Show code')[0].click();
      fixture.detectChanges();
      const hide = buttonsLabelled('Hide code')[0];
      hide.focus();

      hide.click();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(buttonsLabelled('Show code')[0]);
    });

    it('stays with the card when the code hides itself after a minute', async () => {
      vi.useFakeTimers();
      show();
      buttonsLabelled('Show code')[0].click();
      fixture.detectChanges();
      buttonsLabelled('Copy code')[0].focus();

      vi.advanceTimersByTime(REVEAL_MS);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(buttonsLabelled('Show code')[0]);
    });

    it('does not take focus from somewhere else on the page when a code hides', async () => {
      vi.useFakeTimers();
      show();
      buttonsLabelled('Show code')[0].click();
      fixture.detectChanges();
      const field = root.querySelector('input') as HTMLInputElement;
      field.focus();

      vi.advanceTimersByTime(REVEAL_MS);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(field);
    });
  });

  it('hides every shown code when the search changes', () => {
    search('amy');
    httpMock.expectOne(isList).flush([code()]);
    fixture.detectChanges();
    buttonsLabelled('Show code')[0].click();
    fixture.detectChanges();

    search('bob');
    httpMock.expectOne(isList).flush([code({ challengeId: 'c9', destination: 'bob@example.com' })]);
    fixture.detectChanges();

    expect(root.querySelector('.otp-code')).toBeNull();
    expect(root.textContent).toContain('bob@example.com');
  });

  it('shows the API reason when the search fails, and no code', () => {
    search('amy');
    httpMock.expectOne(isList).flush({ title: 'forbidden', detail: 'No access.' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('No access.');
    expect(root.querySelector('.otp-code')).toBeNull();
  });

  it('says so when no usable code matches', () => {
    search('amy');
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();

    expect(root.textContent).toContain('No usable code matches');
  });
});
