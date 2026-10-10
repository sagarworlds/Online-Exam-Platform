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

  /**
   * Answers what the App asks for when it starts: the health check and, when someone is signed in, the unread notification count the
   * header reads (FR-39). Both are answered here, so each test is left with only the requests it is about.
   */
  function answerStartupRequests(): void {
    httpMock
      .match((request) => request.url.endsWith('/v1/health') || request.url.endsWith('/unread-count'))
      .forEach((request) =>
        request.flush(request.request.url.endsWith('/unread-count') ? { unreadCount: 0 } : 'Healthy'),
      );
  }

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(() => true).flush('Healthy');

    expect(fixture.componentInstance).toBeTruthy();
  });

  describe('skip link', () => {
    it('is the first stop for a keyboard, and jumps to the main content', () => {
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      httpMock.expectOne(() => true).flush('Healthy');
      const root = fixture.nativeElement as HTMLElement;

      const skip = root.querySelector<HTMLAnchorElement>('a.skip-link');
      expect(root.querySelector('a, button')).toBe(skip);
      expect(skip?.textContent?.trim()).toBe('Skip to main content');
      expect(skip?.getAttribute('href')).toBe('#main-content');
      expect(root.querySelector('main#main-content')?.getAttribute('tabindex')).toBe('-1');
    });
  });

  describe('API status dot', () => {
    function dot(fixture: { nativeElement: unknown }): HTMLElement {
      return (fixture.nativeElement as HTMLElement).querySelector('.health-dot') as HTMLElement;
    }

    it('is a neutral dot while the API is being checked', () => {
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();

      expect(dot(fixture).classList).toContain('health-dot--checking');
      expect(dot(fixture).title).toBe('Checking API…');
      httpMock.expectOne(() => true).flush('Healthy');
    });

    it('is a green dot, with the words on hover, once the API responds', async () => {
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      httpMock.expectOne(() => true).flush('Healthy');
      await fixture.whenStable();
      fixture.detectChanges();

      expect(dot(fixture).classList).toContain('health-dot--healthy');
      expect(dot(fixture).title).toBe('API is healthy.');
      expect(dot(fixture).getAttribute('aria-label')).toBe('API is healthy.');
      expect((fixture.nativeElement as HTMLElement).querySelector('.health-widget')?.textContent?.trim()).toBe('');
    });

    it('is a red dot, with the reason on hover, when the API cannot be reached', async () => {
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      httpMock.expectOne(() => true).error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
      await fixture.whenStable();
      fixture.detectChanges();

      expect(dot(fixture).classList).toContain('health-dot--unhealthy');
      expect(dot(fixture).title).toMatch(/^API is unreachable: .+/);
      expect(dot(fixture).getAttribute('aria-label')).toBe(dot(fixture).title);
    });
  });

  it('shows the navigation in the language the user picks, with no reload', async () => {
    localStorage.clear();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(() => true).flush('Healthy');
    const compiled = fixture.nativeElement as HTMLElement;
    const picker = compiled.querySelector('select.language-switcher') as HTMLSelectElement;

    picker.value = 'hi';
    picker.dispatchEvent(new Event('change'));
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(compiled.querySelector('header.nav nav')?.textContent).toContain('लॉग इन');
    });
    expect(compiled.querySelector('header.nav nav')?.textContent).not.toContain('Log in');
    expect(document.documentElement.lang).toBe('hi');
    localStorage.clear();
  });

  it('shows login/register links when signed out', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(() => true).flush('Healthy');

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Log in');
    expect(compiled.textContent).toContain('Register');
  });

  describe('when signed in', () => {
    const signIn = (permissions: string[]) =>
      localStorage.setItem(
        AUTH_TOKEN_STORAGE_KEY,
        buildFakeJwt({ sub: 'user-1', exp: Math.floor(Date.now() / 1000) + 3600, perm: permissions }),
      );

    function topNavLinks(): string[] {
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      answerStartupRequests();
      const nav = (fixture.nativeElement as HTMLElement).querySelector('header.nav nav') as HTMLElement;
      return Array.from(nav.querySelectorAll('a')).map((a) => a.textContent?.trim() ?? '');
    }

    function render(): HTMLElement {
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      answerStartupRequests();
      return fixture.nativeElement as HTMLElement;
    }

    it('shows the unread notification count beside Notifications', () => {
      signIn([]);
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      httpMock.expectOne(`${environment.apiBaseUrl}/v1/health`).flush('Healthy');
      httpMock.expectOne((request) => request.url.endsWith('/unread-count')).flush({ unreadCount: 3 });
      fixture.detectChanges();

      const link = (fixture.nativeElement as HTMLElement).querySelector('a.nav-notifications');
      expect(link?.querySelector('.nav-badge')?.textContent?.trim()).toBe('3');
      expect(link?.textContent).toContain('3 unread');
    });

    it('shows a candidate their exams, no admin sidebar', () => {
      signIn([]);

      const links = topNavLinks();

      expect(links).toContain('My exams');
      expect(links).toContain('Redeem invite');
      expect(render().querySelector('.admin-sidebar')).toBeNull();
    });

    it('shows an administrator a sidebar with the admin areas their permissions open', () => {
      signIn(['question.manage', 'question.read', 'exam.read', 'invite.manage']);

      const compiled = render();

      const sidebarLinks = Array.from(compiled.querySelectorAll('.admin-sidebar a')).map((a) => a.getAttribute('aria-label'));
      expect(sidebarLinks).toEqual(expect.arrayContaining(['Questions', 'Exams', 'Invites']));
      expect(sidebarLinks).not.toContain('Batches');
      expect(sidebarLinks).not.toContain('Guardians');
      // The top nav keeps only the links every signed-in user gets, admin or not.
      expect(topNavLinks()).not.toContain('Questions');
    });
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
      answerStartupRequests();
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
