import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { OutstandingOtp } from './otp-codes.models';
import { OtpCodes } from './otp-codes';

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

  afterEach(() => httpMock.verify());

  function search(text: string): void {
    const input = root.querySelector('input')!;
    input.value = text;
    input.dispatchEvent(new Event('input'));
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  it('shows no code until a search is made', () => {
    httpMock.expectNone(isList);
    expect(root.querySelector('.otp-code')).toBeNull();
  });

  it('searches by the text typed and shows each code with where it was sent', () => {
    search('  amy  ');

    const request = httpMock.expectOne(isList);
    expect(request.request.params.get('destination')).toBe('amy');
    request.flush([code(), code({ challengeId: 'c2', destination: '+15550100', channel: 'Sms', purpose: 'Registration', code: '654321' })]);
    fixture.detectChanges();

    const text = root.textContent;
    expect(text).toContain('amy@example.com');
    expect(text).toContain('123456');
    expect(text).toContain('Registration · Sms');
    expect(root.querySelectorAll('.otp-code').length).toBe(2);
  });

  it('lists everything when the search is empty', () => {
    search('');

    const request = httpMock.expectOne(isList);
    expect(request.request.params.has('destination')).toBe(false);
    request.flush([]);
    fixture.detectChanges();

    expect(root.textContent).toContain('No usable code matches');
  });

  it("shows the API's reason when the search fails and no stale codes", () => {
    search('amy');
    httpMock.expectOne(isList).flush({ title: 'forbidden', detail: 'No access.' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('No access.');
    expect(root.querySelector('.otp-code')).toBeNull();
  });
});
