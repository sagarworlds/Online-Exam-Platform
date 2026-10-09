import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { vi } from 'vitest';
import { AuthSessionService } from './auth-session.service';
import { permissionGuard } from './permission.guard';

function setup(isAuthenticated: boolean, permissions: string[]) {
  const createUrlTree = vi.fn((commands: unknown[]) => `tree:${String(commands[0])}`);
  TestBed.configureTestingModule({
    providers: [
      {
        provide: AuthSessionService,
        useValue: { isAuthenticated: () => isAuthenticated, hasPermission: (code: string) => permissions.includes(code) },
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
