import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { vi } from 'vitest';
import { AuthSessionService } from './auth-session.service';
import { anyPermissionGuard, permissionGuard } from './permission.guard';

function setup(isAuthenticated: boolean, permissions: string[]) {
  const createUrlTree = vi.fn((commands: unknown[]) => `tree:${String(commands[0])}`);
  TestBed.configureTestingModule({
    providers: [
      {
        provide: AuthSessionService,
        useValue: {
          isAuthenticated: () => isAuthenticated,
          hasPermission: (code: string) => permissions.includes(code),
          hasAnyPermission: (codes: readonly string[]) => codes.some((code) => permissions.includes(code)),
        },
      },
      { provide: Router, useValue: { createUrlTree } },
    ],
  });
  return { createUrlTree };
}

describe('permissionGuard', () => {
  const route = {} as unknown as ActivatedRouteSnapshot;
  const state = { url: '/exams/create' } as unknown as RouterStateSnapshot;
  const run = () => TestBed.runInInjectionContext(() => permissionGuard('exam.manage')(route, state));

  it('lets a user holding the permission through', () => {
    setup(true, ['exam.manage']);
    expect(run()).toBe(true);
  });

  it('sends a signed-in user without the permission to /forbidden', () => {
    const { createUrlTree } = setup(true, ['exam.read']);

    expect(run()).toBe('tree:/forbidden');
    expect(createUrlTree).toHaveBeenCalledWith(['/forbidden']);
  });

  it('sends a signed-out visitor to /login, remembering where they were going', () => {
    const { createUrlTree } = setup(false, []);

    expect(run()).toBe('tree:/login');
    expect(createUrlTree).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/exams/create' } });
  });
});

describe('anyPermissionGuard', () => {
  const route = {} as unknown as ActivatedRouteSnapshot;
  const state = { url: '/admin/settings' } as unknown as RouterStateSnapshot;
  const run = (permissions: readonly string[] = ['exam.manage', 'identity.otp.read']) =>
    TestBed.runInInjectionContext(() => anyPermissionGuard(permissions)(route, state));

  it.each(['exam.manage', 'identity.otp.read'])('lets a user holding just %s through', (permission) => {
    setup(true, [permission]);
    expect(run()).toBe(true);
  });

  it('lets a user holding several of them through', () => {
    setup(true, ['exam.manage', 'identity.otp.read']);
    expect(run()).toBe(true);
  });

  it('sends a signed-in user holding none of them to /forbidden, whatever else they may do', () => {
    const { createUrlTree } = setup(true, ['exam.read', 'question.manage']);

    expect(run()).toBe('tree:/forbidden');
    expect(createUrlTree).toHaveBeenCalledWith(['/forbidden']);
  });

  it('lets nobody in when no permission opens the page', () => {
    setup(true, ['exam.manage']);
    expect(run([])).toBe('tree:/forbidden');
  });

  it('sends a signed-out visitor to /login, remembering where they were going', () => {
    const { createUrlTree } = setup(false, ['exam.manage']);

    expect(run()).toBe('tree:/login');
    expect(createUrlTree).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/admin/settings' } });
  });
});
