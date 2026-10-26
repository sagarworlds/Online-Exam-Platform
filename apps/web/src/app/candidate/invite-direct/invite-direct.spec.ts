import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { environment } from '../../../environments/environment';
import { AUTH_TOKEN_STORAGE_KEY, AuthSessionService } from '../../auth/auth-session.service';
import { buildFakeJwt } from '../../auth/testing/fake-jwt';
import { InviteDirect } from './invite-direct';

describe('InviteDirect', () => {
  const url = `${environment.apiBaseUrl}/v1/invites/direct-sign-in`;
  const code = 'ABCD1234EFGH5678';
  const examId = '3f2a9c1e-0b7d-4c55-9a60-1d2e3f4a5b6c';
  const newToken = () => buildFakeJwt({ sub: 'student-1', exp: Math.floor(Date.now() / 1000) + 3600 });

  beforeEach(() => {
    localStorage.clear();
  });

  /** Opens the page as the router would for `/invite/direct#<fragment>`, with the address bar's history replaced by a spy. */
  function open(fragment: string | null) {
    const replaceState = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { fragment } } },
        { provide: Location, useValue: { path: () => '/invite/direct', replaceState } },
      ],
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(InviteDirect);
    fixture.detectChanges();
    return {
      fixture,
      navigate,
      replaceState,
      httpMock: TestBed.inject(HttpTestingController),
      session: TestBed.inject(AuthSessionService),
      text: () => (fixture.nativeElement as HTMLElement).textContent ?? '',
      html: () => fixture.nativeElement as HTMLElement,
    };
  }

  function problem(title: string, status: number) {
    return { body: { status, title }, init: { status, statusText: title } };
  }

  it('asks the API to open the link once, as soon as it opens, and says so while it works', () => {
    const { httpMock, text } = open(`code=${code}`);

    const request = httpMock.expectOne(url);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ code });
    expect(text()).toContain('Signing you in');
    httpMock.verify();
  });

  it('takes the code off the address as soon as it has read it', () => {
    const { replaceState, httpMock } = open(`code=${code}`);

    expect(replaceState).toHaveBeenCalledExactlyOnceWith('/invite/direct');
    httpMock.expectOne(url);
  });

  it('signs the student in and goes to the start page of their exam, replacing the link in the history', () => {
    const { httpMock, navigate, session } = open(`code=${code}`);
    const token = newToken();

    httpMock.expectOne(url).flush({ accessToken: token, sessionId: 'session-1', examId });

    expect(session.isAuthenticated()).toBe(true);
    expect(session.accessToken).toBe(token);
    expect(navigate).toHaveBeenCalledExactlyOnceWith(`/my-exams/${examId}/start`, { replaceUrl: true });
  });

  it('replaces whoever was signed in in this browser', () => {
    localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, buildFakeJwt({ sub: 'someone-else', exp: Math.floor(Date.now() / 1000) + 3600 }));
    const { httpMock, session } = open(`code=${code}`);
    expect(session.session()?.userId).toBe('someone-else');

    httpMock.expectOne(url).flush({ accessToken: newToken(), sessionId: 'session-1', examId });

    expect(session.session()?.userId).toBe('student-1');
  });

  it('keeps the signed-in user when the link does not work', () => {
    localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, buildFakeJwt({ sub: 'someone-else', exp: Math.floor(Date.now() / 1000) + 3600 }));
    const { httpMock, session } = open(`code=${code}`);

    const { body, init } = problem('invalid_invite_code', 400);
    httpMock.expectOne(url).flush(body, init);

    expect(session.session()?.userId).toBe('someone-else');
  });

  it.each([
    ['invalid_invite_code', 400, 'already been used or has expired'],
    ['invite_state_invalid', 409, 'already been used or has expired'],
    ['account_locked', 403, 'cannot sign in right now'],
    ['direct_sign_in_not_allowed', 403, 'cannot sign you in'],
    ['direct_sign_in_disabled', 403, 'switched off'],
  ])('says what is wrong, and offers to sign in another way, for %s', (title, status, wording) => {
    const { fixture, httpMock, html, navigate } = open(`code=${code}`);

    const { body, init } = problem(title, status);
    httpMock.expectOne(url).flush(body, init);
    fixture.detectChanges();

    expect(html().querySelector('[role="alert"]')?.textContent).toContain(wording);
    const signIn = html().querySelector('a[href^="/login"]') as HTMLAnchorElement;
    expect(signIn.getAttribute('href')).toContain('returnUrl=%2Fmy-exams');
    expect(html().querySelector('button')).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });

  it.each([
    ['too many attempts', 429, 'rate_limited', 'Too many attempts'],
    ['a lost race', 409, 'concurrency_conflict', 'Too many attempts'],
    ['a server error', 500, 'something_else', 'could not reach the server'],
  ])('offers to try again after %s, and asks again with the same code', (_name, status, title, wording) => {
    const { fixture, httpMock, html } = open(`code=${code}`);

    const { body, init } = problem(title, status);
    httpMock.expectOne(url).flush(body, init);
    fixture.detectChanges();
    expect(html().querySelector('[role="alert"]')?.textContent).toContain(wording);
    (html().querySelector('button') as HTMLButtonElement).click();

    const again = httpMock.expectOne(url);
    expect(again.request.body).toEqual({ code });
  });

  it('treats a request that never reached the server as a failure to try again', () => {
    const { fixture, httpMock, html } = open(`code=${code}`);

    httpMock.expectOne(url).error(new ProgressEvent('error'), { status: 0 });
    fixture.detectChanges();

    expect(html().querySelector('[role="alert"]')?.textContent).toContain('could not reach the server');
    expect(html().querySelector('button')).not.toBeNull();
  });

  it.each([[null], [''], ['other=1'], ['code='], ['code=%20%20']])(
    'sends nothing and says the link is incomplete when the fragment (%s) has no code',
    (fragment) => {
      const { fixture, httpMock, html } = open(fragment);
      fixture.detectChanges();

      httpMock.expectNone(url);
      expect(html().querySelector('[role="alert"]')?.textContent).toContain('incomplete');
    },
  );

  it('leaves the address alone when the page was opened with no fragment at all', () => {
    const { replaceState } = open(null);

    expect(replaceState).not.toHaveBeenCalled();
  });
});
