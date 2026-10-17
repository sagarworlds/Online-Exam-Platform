import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { SETTINGS_PAGES } from './admin-sections';
import { AuthSessionService } from './auth-session.service';
import { landingGuard, landingRoute } from './landing-route';

const sessionWith = (isAuthenticated: boolean, permissions: string[]) =>
  ({
    isAuthenticated: () => isAuthenticated,
    hasAnyPermission: (codes: readonly string[]) => codes.some((code) => permissions.includes(code)),
  }) as unknown as AuthSessionService;

describe('landingRoute', () => {
  it('sends a candidate, who holds no permissions, to their exams', () => {
    expect(landingRoute(sessionWith(true, []))).toBe('/my-exams');
  });

  it.each(['question.manage', 'question.read', 'exam.read', 'exam.manage', 'student.read', 'batch.manage', 'guardian.link.manage'])(
    'sends a user holding %s to the admin home',
    (permission) => {
      expect(landingRoute(sessionWith(true, [permission]))).toBe('/admin');
    },
  );

  // These open only pages gathered under Settings, so they have no entry of their own in the menu: the Settings entry is what counts.
  it.each(['identity.otp.read', 'admin.whatsapp.test'])('sends a user whose only permission is %s, a Settings page, to the admin home', (permission) => {
    expect(landingRoute(sessionWith(true, [permission]))).toBe('/admin');
  });

  it('sends the holder of any one Settings page’s permission to the admin home, so a page added there cannot strand its users on My exams', () => {
    for (const page of SETTINGS_PAGES) {
      for (const permission of page.permissions) {
        expect(landingRoute(sessionWith(true, [permission])), page.path).toBe('/admin');
      }
    }
  });

  it('ignores permissions that open no admin area', () => {
    expect(landingRoute(sessionWith(true, ['admin.audit.read']))).toBe('/my-exams');
  });
});

describe('landingGuard', () => {
  const route = {} as unknown as ActivatedRouteSnapshot;
  const state = { url: '/' } as unknown as RouterStateSnapshot;

  function redirectTarget(isAuthenticated: boolean, permissions: string[]): string {
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthSessionService, useValue: sessionWith(isAuthenticated, permissions) },
        { provide: Router, useValue: { parseUrl: (url: string) => `url:${url}` } },
      ],
    });
    return TestBed.runInInjectionContext(() => landingGuard(route, state)) as unknown as string;
  }

  it('sends a signed-out visitor to /login', () => {
    expect(redirectTarget(false, [])).toBe('url:/login');
  });

  it('sends a signed-in candidate to /my-exams', () => {
    expect(redirectTarget(true, [])).toBe('url:/my-exams');
  });

  it('sends a signed-in administrator to /admin', () => {
    expect(redirectTarget(true, ['exam.manage', 'exam.read'])).toBe('url:/admin');
  });
});
