import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
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
