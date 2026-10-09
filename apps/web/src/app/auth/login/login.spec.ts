import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { buildFakeJwt } from '../testing/fake-jwt';
import { environment } from '../../../environments/environment';
import { Login } from './login';

describe('Login', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });

  it('creates and renders the login form', () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance).toBeTruthy();
    expect(compiled.textContent).toContain('Log in');
  });

  it('links to the privacy policy, a static page that opens in a new tab', () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a[href="/privacy.html"]') as HTMLAnchorElement;

    expect(link.textContent?.trim()).toBe('Privacy policy');
    expect(link.target).toBe('_blank');
    expect(link.rel).toContain('noopener');
  });

  it('switches to the one-time-code form', () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();

    fixture.componentInstance['setMode']('otp');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Send code');
  });

  describe('after a successful password login', () => {
    const tokenWith = (permissions: string[]) =>
      buildFakeJwt({ sub: 'user-1', exp: Math.floor(Date.now() / 1000) + 3600, perm: permissions });

    async function signIn(accessToken: string, queryParams: Record<string, string> = {}): Promise<string> {
      localStorage.clear();
      TestBed.resetTestingModule();
      await TestBed.configureTestingModule({
        imports: [Login],
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          provideRouter([]),
          { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } } },
        ],
      }).compileComponents();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      const fixture = TestBed.createComponent(Login);
      fixture.detectChanges();
      fixture.componentInstance['passwordForm'].setValue({ email: 'a@b.com', password: 'secret' });

      fixture.componentInstance['submitPassword']();
      TestBed.inject(HttpTestingController).expectOne((r) => r.url.endsWith('/auth/login')).flush({ accessToken });

      return navigate.mock.calls[0][0] as string;
    }

    it('takes a candidate to their exams', async () => {
      expect(await signIn(tokenWith([]))).toBe('/my-exams');
    });

    it('takes an administrator to the admin home', async () => {
      expect(await signIn(tokenWith(['exam.manage', 'exam.read']))).toBe('/admin');
    });

    it('goes back to the page the user was heading to, whoever they are', async () => {
      expect(await signIn(tokenWith([]), { returnUrl: '/invite?code=ABCD2345' })).toBe('/invite?code=ABCD2345');
    });
  });

  it('tells staff that the one-time-code form is not for them', () => {
    const fixture = TestBed.createComponent(Login);
    fixture.componentInstance['setMode']('otp');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const input = compiled.querySelector('#login-otp-destination') as HTMLInputElement;
    const hint = compiled.querySelector(`#${input.getAttribute('aria-describedby')}`);
    expect(hint?.textContent).toContain('Staff and admin accounts sign in with a password and a one-time code.');
  });

  it('requesting an OTP navigates without putting the destination in the URL', () => {
    const httpMock = TestBed.inject(HttpTestingController);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(Login);
    fixture.componentInstance['setMode']('otp');
    fixture.componentInstance['otpForm'].setValue({ destination: 'jane@example.com' });

    fixture.componentInstance['submitOtpRequest']();
    httpMock.expectOne(`${environment.apiBaseUrl}/v1/auth/otp/request`).flush({ otpChallengeId: 'challenge-1' });

    expect(navigate).toHaveBeenCalledTimes(1);
    const [commands, extras] = navigate.mock.calls[0];
    expect(commands).toEqual(['/verify-otp']);
    expect(extras?.queryParams).toEqual({ challengeId: 'challenge-1', purpose: 'Login' });
    expect(extras?.state).toEqual({ destination: 'jane@example.com' });
    httpMock.verify();
  });

  describe('session-ended banner', () => {
    function createWithQueryParams(params: Record<string, string>) {
      TestBed.overrideProvider(ActivatedRoute, {
        useValue: { snapshot: { queryParamMap: convertToParamMap(params) } },
      });
      const fixture = TestBed.createComponent(Login);
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    it('shows the signed-in-elsewhere banner for reason=session_superseded', () => {
      const compiled = createWithQueryParams({ reason: 'session_superseded' });

      const banner = compiled.querySelector('[role="status"]');
      expect(banner?.textContent).toContain('your account signed in on another device');
    });

    it('shows no banner for an unrecognised reason', () => {
      const compiled = createWithQueryParams({ reason: '<b>not-a-reason</b>' });

      expect(compiled.querySelector('[role="status"]')).toBeNull();
    });
  });

  describe('the sign-in form', () => {
    function render() {
      const fixture = TestBed.createComponent(Login);
      fixture.detectChanges();
      return { fixture, compiled: fixture.nativeElement as HTMLElement };
    }

    it('says what is missing when submitted empty, and leaves the button usable', () => {
      const { fixture, compiled } = render();

      fixture.componentInstance['submitPassword']();
      fixture.detectChanges();

      expect(compiled.querySelector('#login-email-error')?.textContent).toContain('Enter your email address.');
      expect(compiled.querySelector('#login-password-error')?.textContent).toContain('Enter your password.');
      expect((compiled.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(false);
    });

    it('explains a malformed email differently from a missing one', () => {
      const { fixture, compiled } = render();
      fixture.componentInstance['passwordForm'].setValue({ email: 'not-an-email', password: 'secret' });

      fixture.componentInstance['submitPassword']();
      fixture.detectChanges();

      expect(compiled.querySelector('#login-email-error')?.textContent).toContain('name@example.com');
    });

    it('announces a failed sign-in as an alert, directly above the submit button', () => {
      const { fixture, compiled } = render();
      fixture.componentInstance['passwordForm'].setValue({ email: 'a@b.com', password: 'wrong' });

      fixture.componentInstance['submitPassword']();
      TestBed.inject(HttpTestingController)
        .expectOne((r) => r.url.endsWith('/auth/login'))
        .flush({ detail: 'The email or password is incorrect.' }, { status: 401, statusText: 'Unauthorized' });
      fixture.detectChanges();

      const alert = compiled.querySelector('[role="alert"]');
      expect(alert?.textContent).toContain('The email or password is incorrect.');
      expect(alert?.nextElementSibling?.getAttribute('type')).toBe('submit');
    });

    it('shows the password as text only while the user asks to see it', () => {
      const { fixture, compiled } = render();
      const password = compiled.querySelector('#login-password') as HTMLInputElement;
      const toggle = compiled.querySelector('.password-toggle') as HTMLButtonElement;

      expect(password.type).toBe('password');
      expect(toggle.textContent?.trim()).toBe('Show');

      toggle.click();
      fixture.detectChanges();

      expect(password.type).toBe('text');
      expect(toggle.textContent?.trim()).toBe('Hide');
    });
  });
});
