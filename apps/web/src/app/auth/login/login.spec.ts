import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { buildFakeJwt } from '../testing/fake-jwt';
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
});
