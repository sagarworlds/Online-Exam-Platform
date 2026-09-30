import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { environment } from '../environments/environment';
import { App } from './app';
import { AUTH_TOKEN_STORAGE_KEY, AuthSessionService } from './auth/auth-session.service';
import { buildFakeJwt } from './auth/testing/fake-jwt';

describe('App', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(() => true).flush('Healthy');

    expect(fixture.componentInstance).toBeTruthy();
  });

  it('shows a healthy status once the API responds', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(() => true).flush('Healthy');
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('API is healthy.');
  });

  it('shows login/register links when signed out', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(() => true).flush('Healthy');

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Log in');
    expect(compiled.textContent).toContain('Register');
  });

  describe('logout', () => {
    let authSession: AuthSessionService;
    let navigateByUrl: ReturnType<typeof vi.spyOn>;

    function renderSignedIn(): HTMLElement {
      authSession = TestBed.inject(AuthSessionService);
      authSession.login(buildFakeJwt({ sub: 'user-1', sid: 'session-1', exp: Math.floor(Date.now() / 1000) + 3600 }));
      navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      httpMock.expectOne(`${environment.apiBaseUrl}/v1/health`).flush('Healthy');
      return fixture.nativeElement as HTMLElement;
    }

    function clickLogOut(compiled: HTMLElement): void {
      const button = Array.from(compiled.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Log out');
      button?.click();
    }

    it('logout posts to /v1/auth/logout and clears the session', () => {
      const compiled = renderSignedIn();

      clickLogOut(compiled);
      const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/auth/logout`);
      expect(req.request.method).toBe('POST');
      req.flush(null, { status: 204, statusText: 'No Content' });

      expect(authSession.session()).toBeNull();
      expect(localStorage.getItem(AUTH_TOKEN_STORAGE_KEY)).toBeNull();
      expect(navigateByUrl).toHaveBeenCalledWith('/login');
    });

    it('logout still clears the local session when the server call fails', () => {
      const compiled = renderSignedIn();

      clickLogOut(compiled);
      httpMock
        .expectOne(`${environment.apiBaseUrl}/v1/auth/logout`)
        .flush(null, { status: 503, statusText: 'Service Unavailable' });

      expect(authSession.session()).toBeNull();
      expect(localStorage.getItem(AUTH_TOKEN_STORAGE_KEY)).toBeNull();
      expect(navigateByUrl).toHaveBeenCalledWith('/login');
    });
  });
});
