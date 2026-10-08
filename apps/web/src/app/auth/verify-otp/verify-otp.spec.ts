import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { environment } from '../../../environments/environment';
import { VerifyOtp } from './verify-otp';

describe('VerifyOtp', () => {
  // Real navigations below write their state to jsdom's history, which outlives
  // each test; clear it so a later test can't read an earlier destination.
  afterEach(() => history.replaceState(null, ''));

  async function createWithQueryParams(params: Record<string, string>) {
    await TestBed.configureTestingModule({
      imports: [VerifyOtp],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(params) } } },
      ],
    }).compileComponents();

    return TestBed.createComponent(VerifyOtp);
  }

  /** Navigates to /verify-otp for real, passing the destination as router navigation state. */
  async function navigateWithDestination(purpose: string, destination: string) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'verify-otp', component: VerifyOtp }]),
      ],
    });
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);
    await router.navigate(['/verify-otp'], {
      queryParams: { challengeId: 'challenge-1', purpose },
      state: { destination },
    });
    harness.detectChanges();

    return { router, compiled: harness.routeNativeElement as HTMLElement };
  }

  async function renderForm() {
    const fixture = await createWithQueryParams({ challengeId: 'challenge-1' });
    fixture.detectChanges();
    return { fixture, compiled: fixture.nativeElement as HTMLElement, httpMock: TestBed.inject(HttpTestingController) };
  }

  /** Types into the code input the way a keyboard or a phone's autofill does: set the value, then fire input. */
  function type(fixture: { detectChanges(): void }, compiled: HTMLElement, value: string) {
    const input = compiled.querySelector('#verify-otp-code') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    return input;
  }

  const verifyUrl = `${environment.apiBaseUrl}/v1/auth/otp/verify`;

  it('renders the code form when a challenge id is present, ignoring any destination in the URL', async () => {
    const fixture = await createWithQueryParams({ challengeId: 'challenge-1', destination: 'a@b.com' });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('#verify-otp-code')).toBeTruthy();
    expect(compiled.textContent).not.toContain('a@b.com');
  });

  it('shows the destination from navigation state masked, with copy that does not confirm the account', async () => {
    const { router, compiled } = await navigateWithDestination('Login', 'jane@example.com');

    expect(compiled.textContent).toContain("If an account exists for j***@example.com, we've sent a code.");
    expect(compiled.textContent).not.toContain('jane@example.com');
    expect(router.url).not.toContain('jane');
    expect(compiled.querySelector('#verify-otp-code')).toBeTruthy();
  });

  it('confirms the code was sent after registration', async () => {
    const { compiled } = await navigateWithDestination('Registration', '9876543210');

    expect(compiled.textContent).toContain("We've sent a code to ********10.");
  });

  it('shows an error and no form when the challenge id is missing', async () => {
    const fixture = await createWithQueryParams({});
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Missing verification challenge');
    expect(compiled.querySelector('#verify-otp-code')).toBeNull();
  });

  it('shows each digit typed in its own box, and the next box waiting for a digit is marked', async () => {
    const { fixture, compiled } = await renderForm();

    type(fixture, compiled, '123');

    const boxes = Array.from(compiled.querySelectorAll('.code-entry__box'));
    expect(boxes.map((box) => box.textContent?.trim())).toEqual(['1', '2', '3', '', '', '']);
    expect(boxes[3].classList).toContain('code-entry__box--next');
  });

  it('keeps only digits, and no more than six, so the boxes always show what will be sent', async () => {
    const { fixture, compiled } = await renderForm();

    const input = type(fixture, compiled, '12 a-34 56 789');

    expect(input.value).toBe('123456');
    expect(fixture.componentInstance['form'].controls.code.value).toBe('123456');
  });

  it('asks for six digits when the code is too short, and sends nothing', async () => {
    const { fixture, compiled, httpMock } = await renderForm();

    type(fixture, compiled, '123');
    (compiled.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(compiled.querySelector('#verify-otp-code-error')?.textContent).toContain('Enter the six-digit code.');
    expect(compiled.querySelector('#verify-otp-code')?.getAttribute('aria-invalid')).toBe('true');
    httpMock.expectNone(verifyUrl);
  });

  it('sends a complete code to verify, with the challenge it belongs to', async () => {
    const { fixture, compiled, httpMock } = await renderForm();

    type(fixture, compiled, '123456');
    (compiled.querySelector('button[type="submit"]') as HTMLButtonElement).click();

    const request = httpMock.expectOne(verifyUrl);
    expect(request.request.body).toEqual({ otpChallengeId: 'challenge-1', code: '123456' });
    request.flush({ accessToken: undefined }, { status: 200, statusText: 'OK' });
  });

  it('announces a refused code above the button and keeps the digits that were typed', async () => {
    const { fixture, compiled, httpMock } = await renderForm();

    type(fixture, compiled, '123456');
    (compiled.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    httpMock
      .expectOne(verifyUrl)
      .flush({ detail: 'That code is not right.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    const alert = compiled.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('That code is not right.');
    expect(alert?.nextElementSibling?.getAttribute('type')).toBe('submit');
    expect((compiled.querySelector('#verify-otp-code') as HTMLInputElement).value).toBe('123456');
  });
});
