/** The parts of an access token's payload the UI needs — nav state, expiry, the route guard. */
export interface DecodedSession {
  userId: string;
  sessionId: string | null;
  expiresAtUtc: number;
  /** Role names (e.g. "Candidate", "ExamAdmin"). Display and navigation only — see {@link decodeJwt}. */
  roles: string[];
  /** Permission codes (e.g. "exam.manage"). Display and navigation only — see {@link decodeJwt}. */
  permissions: string[];
}

/**
 * The API writes the role claim under the full .NET claim-type URI; a token from another issuer, or a
 * future API change, may use the short "role". Both are read so the UI does not break on either.
 */
const ROLE_CLAIM_KEYS = ['http://schemas.microsoft.com/ws/2008/06/identity/claims/role', 'role'];
const PERMISSION_CLAIM_KEY = 'perm';

/** A JWT claim holding several values is an array; one holding a single value is a bare string. */
function claimValues(payload: Record<string, unknown>, keys: string[]): string[] {
  const values = keys.flatMap((key) => {
    const claim = payload[key];
    return Array.isArray(claim) ? claim : [claim];
  });
  return [...new Set(values.filter((value): value is string => typeof value === 'string'))];
}

/**
 * Decodes a JWT's payload for UI purposes only — driving the nav bar, the route
 * guard, and session expiry. The signature is never checked client-side; the
 * server is the only authority on whether a token is actually valid, so this
 * must never be used to make an authorization decision — roles and permissions
 * decoded here only decide what the UI offers, and the API re-checks every call.
 */
export function decodeJwt(token: string): DecodedSession | null {
  const payloadSegment = token.split('.')[1];
  if (!payloadSegment) {
    return null;
  }

  try {
    const base64 = payloadSegment.replace(/-/g, '+').replace(/_/g, '/');
    const payload = JSON.parse(atob(base64)) as Record<string, unknown>;
    const userId = payload['sub'];
    const expiresAtUtc = payload['exp'];

    if (typeof userId !== 'string' || typeof expiresAtUtc !== 'number') {
      return null;
    }

    return {
      userId,
      sessionId: typeof payload['sid'] === 'string' ? payload['sid'] : null,
      expiresAtUtc,
      roles: claimValues(payload, ROLE_CLAIM_KEYS),
      permissions: claimValues(payload, [PERMISSION_CLAIM_KEY]),
    };
  } catch {
    return null;
  }
}

/** Whether a decoded session's token has passed its expiry. */
export function isExpired(session: DecodedSession, nowMs = Date.now()): boolean {
  return session.expiresAtUtc * 1000 <= nowMs;
}
