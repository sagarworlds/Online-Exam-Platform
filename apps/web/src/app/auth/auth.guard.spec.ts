import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { vi } from 'vitest';
import { AuthSessionService } from './auth-session.service';
import { authGuard } from './auth.guard';

function setup(isAuthenticated: boolean) {
  const createUrlTree = vi.fn().mockReturnValue('url-tree-stub');
  TestBed.configureTestingModule({
    providers: [
      { provide: AuthSessionService, useValue: { isAuthenticated: () => isAuthenticated } },
      { provide: Router, useValue: { createUrlTree } },
    ],
  });
  return { createUrlTree };
}

describe('authGuard', () => {
  const route = {} as unknown as ActivatedRouteSnapshot;
  const state = { url: '/profile' } as unknown as RouterStateSnapshot;

  it('allows activation when signed in', () => {
    setup(true);
    const result = TestBed.runInInjectionContext(() => authGuard(route, state));
    expect(result).toBe(true);
  });

  it('redirects to login with a returnUrl when signed out', () => {
    const { createUrlTree } = setup(false);
    const result = TestBed.runInInjectionContext(() => authGuard(route, state));

    expect(createUrlTree).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/profile' } });
    expect(result).toBe('url-tree-stub');
  });
});
