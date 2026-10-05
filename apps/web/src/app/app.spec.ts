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
