import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AUTH_TOKEN_STORAGE_KEY } from './auth/auth-session.service';
import { buildFakeJwt } from './auth/testing/fake-jwt';
import { App } from './app';

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
      httpMock.expectOne(() => true).flush('Healthy');
      const nav = (fixture.nativeElement as HTMLElement).querySelector('header.nav nav') as HTMLElement;
      return Array.from(nav.querySelectorAll('a')).map((a) => a.textContent?.trim() ?? '');
    }

    function render(): HTMLElement {
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      httpMock.expectOne(() => true).flush('Healthy');
      return fixture.nativeElement as HTMLElement;
    }

    it('shows a candidate their exams, no admin sidebar', () => {
      signIn([]);

      const links = topNavLinks();

      expect(links).toContain('My exams');
      expect(links).toContain('Redeem invite');
      expect(render().querySelector('.admin-sidebar')).toBeNull();
    });

    it('shows an administrator a sidebar with the admin areas their permissions open', () => {
      signIn(['question.manage', 'exam.read', 'invite.manage']);

      const compiled = render();

      const sidebarLinks = Array.from(compiled.querySelectorAll('.admin-sidebar a')).map((a) => a.getAttribute('aria-label'));
      expect(sidebarLinks).toEqual(expect.arrayContaining(['Questions', 'Exams', 'Invites']));
      expect(sidebarLinks).not.toContain('Batches');
      expect(sidebarLinks).not.toContain('Guardians');
      // The top nav keeps only the links every signed-in user gets, admin or not.
      expect(topNavLinks()).not.toContain('Questions');
    });
  });
});
