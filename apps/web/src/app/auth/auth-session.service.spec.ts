import { TestBed } from '@angular/core/testing';
import { AUTH_TOKEN_STORAGE_KEY, AuthSessionService } from './auth-session.service';
import { buildFakeJwt } from './testing/fake-jwt';

function createService(): AuthSessionService {
  TestBed.configureTestingModule({});
  return TestBed.inject(AuthSessionService);
}

const futureExp = () => Math.floor(Date.now() / 1000) + 3600;
const pastExp = () => Math.floor(Date.now() / 1000) - 10;

describe('AuthSessionService', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  it('starts signed out when nothing is stored', () => {
    const service = createService();
    expect(service.isAuthenticated()).toBe(false);
    expect(service.accessToken).toBeNull();
  });

  it('becomes authenticated after login with a valid token', () => {
    const service = createService();
    const token = buildFakeJwt({ sub: 'user-1', exp: futureExp() });
    service.login(token);

    expect(service.isAuthenticated()).toBe(true);
    expect(service.session()?.userId).toBe('user-1');
    expect(service.accessToken).toBe(token);
  });

  it('throws and does not store an already-expired token', () => {
    const service = createService();
    const token = buildFakeJwt({ sub: 'user-1', exp: pastExp() });

    expect(() => service.login(token)).toThrow();
    expect(service.isAuthenticated()).toBe(false);
  });

  it('clears the session on logout', () => {
    const service = createService();
    service.login(buildFakeJwt({ sub: 'user-1', exp: futureExp() }));

    service.logout();

    expect(service.isAuthenticated()).toBe(false);
    expect(service.accessToken).toBeNull();
  });

  it('rehydrates a valid session from storage on construction', () => {
    const token = buildFakeJwt({ sub: 'user-2', exp: futureExp() });
    localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, token);

    const service = createService();

    expect(service.isAuthenticated()).toBe(true);
    expect(service.session()?.userId).toBe('user-2');
  });

  it('discards an expired stored token on construction', () => {
    localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, buildFakeJwt({ sub: 'user-3', exp: pastExp() }));

    const service = createService();

    expect(service.isAuthenticated()).toBe(false);
    expect(localStorage.getItem(AUTH_TOKEN_STORAGE_KEY)).toBeNull();
  });
});
