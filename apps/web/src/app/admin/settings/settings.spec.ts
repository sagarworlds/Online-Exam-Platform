import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, CanActivateFn, Router, RouterStateSnapshot, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { routes } from '../../app.routes';
import { SETTINGS_PAGES, SETTINGS_SECTION } from '../../auth/admin-sections';
import { AuthSessionService } from '../../auth/auth-session.service';
import { I18nService } from '../../i18n/i18n.service';
import { Settings } from './settings';

/** A user who holds exactly these permissions. */
const holding = (permissions: string[]) => ({
  isAuthenticated: () => true,
  hasPermission: (code: string) => permissions.includes(code),
  hasAnyPermission: (codes: readonly string[]) => codes.some((code) => permissions.includes(code)),
});

function render(permissions: string[]) {
  TestBed.configureTestingModule({
    imports: [Settings],
    providers: [provideRouter([]), { provide: AuthSessionService, useValue: holding(permissions) }],
  });
  const fixture = TestBed.createComponent(Settings);
  fixture.detectChanges();
  return { fixture, page: fixture.nativeElement as HTMLElement };
}

const rowNames = (page: HTMLElement) => Array.from(page.querySelectorAll('.admin-area__name')).map((name) => name.textContent?.trim());
const rowLinks = (page: HTMLElement) => Array.from(page.querySelectorAll('a.admin-area')).map((a) => a.getAttribute('href'));

describe('Settings', () => {
  afterEach(() => localStorage.clear());

  it('has a heading and says who sees what', () => {
    const { page } = render(['exam.manage']);

    expect(page.querySelector('h1')?.textContent?.trim()).toBe('Settings');
    expect(page.querySelector('p.hint')?.textContent).toContain('You see the ones your account may open.');
  });

  it('lists all four pages, in order, to a user who may open them all', () => {
    const { page } = render(['exam.manage', 'admin.whatsapp.test', 'identity.otp.read']);

    expect(rowNames(page)).toEqual(['Branding', 'Instruction templates', 'WhatsApp test', 'Candidate codes']);
  });

  it('links each page at its own address, which the move to Settings did not change', () => {
    const { page } = render(['exam.manage', 'admin.whatsapp.test', 'identity.otp.read']);

    expect(rowLinks(page)).toEqual(['/admin/branding', '/admin/instruction-templates', '/admin/whatsapp', '/admin/otp-codes']);
  });

  it.each([
    ['exam.manage', ['Branding', 'Instruction templates']],
    ['admin.whatsapp.test', ['WhatsApp test']],
    ['identity.otp.read', ['Candidate codes']],
  ])('lists only what a user holding just %s may open', (permission, names) => {
    const { page } = render([permission]);

    expect(rowNames(page)).toEqual(names);
  });

  it('lists the pages for the permissions held, not for others, however many others there are', () => {
    const { page } = render(['question.manage', 'question.read', 'exam.read', 'invite.manage', 'identity.otp.read']);

    expect(rowNames(page)).toEqual(['Candidate codes']);
  });

  it('does not list Settings itself or any other admin area', () => {
    const { page } = render(['exam.manage', 'exam.read', 'question.read', 'admin.whatsapp.test', 'identity.otp.read']);

    expect(rowNames(page)).not.toContain('Settings');
    expect(rowNames(page)).not.toContain('Questions');
    expect(rowLinks(page)).not.toContain('/admin/settings');
  });

  it('reads the page in the language the admin chose', async () => {
    const { fixture, page } = render(['exam.manage']);

    await TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();

    expect(page.querySelector('h1')?.textContent?.trim()).toBe('सेटिंग');
    expect(rowNames(page)).toEqual(['ब्रांडिंग', 'निर्देश टेम्पलेट']);
  });
});

describe('the Settings route', () => {
  const route = routes.find((r) => r.path === 'admin/settings')!;
  const state = { url: '/admin/settings' } as unknown as RouterStateSnapshot;

  /** Runs the guard of the route at `path` for a user with the given permissions (a signed-out visitor when `isAuthenticated` is false). */
  function runGuard(path: string, isAuthenticated: boolean, permissions: string[]) {
    const createUrlTree = vi.fn((commands: unknown[]) => `tree:${String(commands[0])}`);
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthSessionService, useValue: { ...holding(permissions), isAuthenticated: () => isAuthenticated } },
        { provide: Router, useValue: { createUrlTree } },
      ],
    });
    const guard = routes.find((r) => r.path === path)!.canActivate![0] as CanActivateFn;
    return TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, state));
  }

  it('opens the Settings page, at the address the Settings entry has', async () => {
    expect(route).toBeDefined();
    expect(await route.loadComponent!()).toBe(Settings);
    expect(SETTINGS_SECTION.path).toBe(`/${route.path}`);
  });

  it.each(['exam.manage', 'admin.whatsapp.test', 'identity.otp.read'])('lets in a user whose only permission is %s', (permission) => {
    expect(runGuard('admin/settings', true, [permission])).toBe(true);
  });

  it('sends a signed-in user who may open none of the pages to /forbidden, whatever else they may do', () => {
    expect(runGuard('admin/settings', true, ['exam.read', 'question.manage', 'invite.manage'])).toBe('tree:/forbidden');
  });

  it('sends a signed-out visitor to /login', () => {
    expect(runGuard('admin/settings', false, [])).toBe('tree:/login');
  });

  describe.each(SETTINGS_PAGES.map((page) => [page.path, page] as const))('the page at %s', (path, page) => {
    it('keeps its own route, so a bookmark to it still works', () => {
      expect(routes.find((r) => r.path === path.slice(1))).toBeDefined();
    });

    it.each(page.permissions)('is still opened by %s, the permission Settings lists it under', (permission) => {
      expect(runGuard(path.slice(1), true, [permission])).toBe(true);
    });

    it('is still closed to a user holding none of its permissions', () => {
      expect(runGuard(path.slice(1), true, ['question.read'])).toBe('tree:/forbidden');
    });
  });
});
