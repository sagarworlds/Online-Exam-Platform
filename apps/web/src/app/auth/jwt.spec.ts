import { buildFakeJwt } from './testing/fake-jwt';
import { decodeJwt, isExpired } from './jwt';

describe('decodeJwt', () => {
  it('decodes sub, sid, and exp from a well-formed token', () => {
    const token = buildFakeJwt({ sub: 'user-1', sid: 'session-1', exp: 1234567890 });
    expect(decodeJwt(token)).toEqual({ userId: 'user-1', sessionId: 'session-1', expiresAtUtc: 1234567890 });
  });

  it('defaults sessionId to null when sid is absent', () => {
    const token = buildFakeJwt({ sub: 'user-1', exp: 1234567890 });
    expect(decodeJwt(token)?.sessionId).toBeNull();
  });

  it('returns null when sub is missing', () => {
    const token = buildFakeJwt({ exp: 1234567890 });
    expect(decodeJwt(token)).toBeNull();
  });

  it('returns null when exp is missing', () => {
    const token = buildFakeJwt({ sub: 'user-1' });
    expect(decodeJwt(token)).toBeNull();
  });

  it('returns null for a malformed token', () => {
    expect(decodeJwt('not-a-jwt')).toBeNull();
  });
});

describe('isExpired', () => {
  it('is false before expiry and true at/after it', () => {
    const session = { userId: 'u', sessionId: null, expiresAtUtc: 1000 };
    expect(isExpired(session, 999_000)).toBe(false);
    expect(isExpired(session, 1_000_000)).toBe(true);
  });
});
