import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
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
});
