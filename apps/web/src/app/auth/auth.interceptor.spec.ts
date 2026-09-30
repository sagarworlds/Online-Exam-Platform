import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { AuthSessionService } from './auth-session.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let logout: ReturnType<typeof vi.fn>;
  let navigateByUrl: ReturnType<typeof vi.fn>;

  function setup(accessToken: string | null): void {
    logout = vi.fn();
    navigateByUrl = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthSessionService, useValue: { accessToken, logout } },
        { provide: Router, useValue: { navigateByUrl } },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  it('attaches a bearer token when a session exists', () => {
    setup('token-123');
    http.get('/v1/me/profile').subscribe();

    const req = httpMock.expectOne('/v1/me/profile');
    expect(req.request.headers.get('Authorization')).toBe('Bearer token-123');
    req.flush({});
  });

  it('sends no Authorization header when signed out', () => {
    setup(null);
    http.get('/v1/health').subscribe();

    const req = httpMock.expectOne('/v1/health');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('redirects to /login?reason=session_superseded when the API says the session was superseded', () => {
    setup('token-123');
    http.get('/v1/me/profile').subscribe({ error: () => undefined });

    const req = httpMock.expectOne('/v1/me/profile');
    req.flush(
      { status: 401, title: 'session_superseded', detail: 'This session was replaced by a newer sign-in.' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(logout).toHaveBeenCalled();
    expect(navigateByUrl).toHaveBeenCalledWith('/login?reason=session_superseded');
  });

  it('logs out and redirects to plain /login on a 401 without a known session reason', () => {
    setup('token-123');
    http.get('/v1/me/profile').subscribe({ error: () => undefined });

    const req = httpMock.expectOne('/v1/me/profile');
    req.flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(logout).toHaveBeenCalled();
    expect(navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('does not log out on a 401 for an unauthenticated request (e.g. bad login credentials)', () => {
    setup(null);
    http.post('/v1/auth/login', {}).subscribe({ error: () => undefined });

    const req = httpMock.expectOne('/v1/auth/login');
    req.flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(logout).not.toHaveBeenCalled();
    expect(navigateByUrl).not.toHaveBeenCalled();
  });
});
