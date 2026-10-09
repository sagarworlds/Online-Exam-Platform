import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, vi } from 'vitest';
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
