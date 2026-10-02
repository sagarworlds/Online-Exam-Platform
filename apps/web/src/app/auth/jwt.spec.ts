import { buildFakeJwt } from './testing/fake-jwt';
import { decodeJwt, isExpired } from './jwt';

describe('decodeJwt', () => {
  it('decodes sub, sid, and exp from a well-formed token', () => {
    const token = buildFakeJwt({ sub: 'user-1', sid: 'session-1', exp: 1234567890 });
    expect(decodeJwt(token)).toEqual({
      userId: 'user-1',
      sessionId: 'session-1',
      expiresAtUtc: 1234567890,
      roles: [],
      permissions: [],
    });
  });

  it('reads several roles and permissions from the claims the API writes', () => {
    const token = buildFakeJwt({
      sub: 'user-1',
      exp: 1234567890,
      'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': ['ExamAdmin', 'ContentAuthor'],
      perm: ['exam.manage', 'question.manage'],
    });

    const session = decodeJwt(token);

    expect(session?.roles).toEqual(['ExamAdmin', 'ContentAuthor']);
    expect(session?.permissions).toEqual(['exam.manage', 'question.manage']);
  });

  it('reads a single role and a single permission, which JWT writes as bare strings', () => {
    const token = buildFakeJwt({
      sub: 'user-1',
      exp: 1234567890,
      'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'Candidate',
      perm: 'exam.read',
    });

    const session = decodeJwt(token);

    expect(session?.roles).toEqual(['Candidate']);
    expect(session?.permissions).toEqual(['exam.read']);
  });

  it('also accepts the short "role" claim and ignores values that are not strings', () => {
    const token = buildFakeJwt({ sub: 'user-1', exp: 1234567890, role: ['Candidate', 7, null], perm: [{}] });

    const session = decodeJwt(token);

    expect(session?.roles).toEqual(['Candidate']);
    expect(session?.permissions).toEqual([]);
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
    const session = { userId: 'u', sessionId: null, expiresAtUtc: 1000, roles: [], permissions: [] };
    expect(isExpired(session, 999_000)).toBe(false);
    expect(isExpired(session, 1_000_000)).toBe(true);
  });
});
