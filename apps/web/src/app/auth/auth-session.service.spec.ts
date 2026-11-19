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

  it('answers permission questions from the signed-in token, and "no" when signed out', () => {
    const service = createService();
    expect(service.hasPermission('exam.read')).toBe(false);

    service.login(buildFakeJwt({ sub: 'user-1', exp: futureExp(), perm: ['exam.read', 'exam.manage'] }));

    expect(service.hasPermission('exam.read')).toBe(true);
    expect(service.hasPermission('invite.manage')).toBe(false);
    expect(service.hasAnyPermission(['invite.manage', 'exam.manage'])).toBe(true);
    expect(service.hasAnyPermission(['invite.manage'])).toBe(false);
    expect(service.hasAnyPermission([])).toBe(false);

    service.logout();
    expect(service.hasPermission('exam.read')).toBe(false);
  });

  describe('when the browser refuses the store', () => {
    const refuse = (method: 'getItem' | 'setItem' | 'removeItem') =>
      vi.spyOn(Storage.prototype, method).mockImplementation(() => {
        throw new DOMException('The quota has been exceeded or access is denied.', 'QuotaExceededError');
      });

    afterEach(() => vi.restoreAllMocks());

    it('starts signed out instead of throwing when the stored token cannot be read', () => {
      localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, buildFakeJwt({ sub: 'user-2', exp: futureExp() }));
      refuse('getItem');

      const service = createService();

      expect(service.isAuthenticated()).toBe(false);
      expect(service.accessToken).toBeNull();
    });

    it('starts signed out when merely reaching for localStorage throws, as a browser that blocks site data does', () => {
      vi.spyOn(window, 'localStorage', 'get').mockImplementation(() => {
        throw new DOMException('Access is denied for this document.', 'SecurityError');
      });

      const service = createService();
      expect(service.isAuthenticated()).toBe(false);

      const token = buildFakeJwt({ sub: 'user-1', exp: futureExp(), perm: ['exam.read'] });
      service.login(token);
      expect(service.isAuthenticated()).toBe(true);
      expect(service.accessToken).toBe(token);
      expect(service.hasPermission('exam.read')).toBe(true);

      service.logout();
      expect(service.isAuthenticated()).toBe(false);
      expect(service.accessToken).toBeNull();
    });

    it('still signs the user in for this visit when the token cannot be written, and hands out that token', () => {
      refuse('setItem');
      const service = createService();
      const token = buildFakeJwt({ sub: 'user-1', exp: futureExp(), perm: ['exam.read'] });

      expect(() => service.login(token)).not.toThrow();

      expect(service.isAuthenticated()).toBe(true);
      expect(service.session()?.userId).toBe('user-1');
      expect(service.hasPermission('exam.read')).toBe(true);
      expect(service.accessToken).toBe(token);
    });

    it('hands out the new token, not an older one still in the store, when the new one could not be written', () => {
      localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, buildFakeJwt({ sub: 'earlier', exp: futureExp() }));
      refuse('setItem');
      const service = createService();
      const token = buildFakeJwt({ sub: 'user-1', exp: futureExp() });

      service.login(token);

      expect(service.accessToken).toBe(token);
    });

    it('reads the token from the store again once a later sign-in is written', () => {
      const setItem = refuse('setItem');
      const service = createService();
      service.login(buildFakeJwt({ sub: 'user-1', exp: futureExp() }));
      setItem.mockRestore();
      const second = buildFakeJwt({ sub: 'user-2', exp: futureExp() });

      service.login(second);

      expect(localStorage.getItem(AUTH_TOKEN_STORAGE_KEY)).toBe(second);
      expect(service.accessToken).toBe(second);
    });

    it('keeps handing out the token when the store stops answering after sign-in', () => {
      const service = createService();
      const token = buildFakeJwt({ sub: 'user-1', exp: futureExp() });
      service.login(token);
      refuse('getItem');

      expect(service.accessToken).toBe(token);
    });

    it('keeps handing out a resumed session’s token when the store stops answering later', () => {
      const token = buildFakeJwt({ sub: 'user-2', exp: futureExp() });
      localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, token);
      const service = createService();
      refuse('getItem');

      expect(service.isAuthenticated()).toBe(true);
      expect(service.accessToken).toBe(token);
    });

    it('signs out for this tab when the stored token cannot be removed', () => {
      const service = createService();
      service.login(buildFakeJwt({ sub: 'user-1', exp: futureExp() }));
      refuse('removeItem');
      refuse('getItem');

      expect(() => service.logout()).not.toThrow();

      expect(service.isAuthenticated()).toBe(false);
      expect(service.accessToken).toBeNull();
    });

    it('starts signed out when an expired stored token cannot be removed', () => {
      localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, buildFakeJwt({ sub: 'user-3', exp: pastExp() }));
      refuse('removeItem');

      const service = createService();

      expect(service.isAuthenticated()).toBe(false);
    });
  });
});
