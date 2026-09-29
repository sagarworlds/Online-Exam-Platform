/** The parts of an access token's payload the UI needs — nav state, expiry, the route guard. */
export interface DecodedSession {
  userId: string;
  sessionId: string | null;
  expiresAtUtc: number;
}

/**
 * Decodes a JWT's payload for UI purposes only — driving the nav bar, the route
 * guard, and session expiry. The signature is never checked client-side; the
 * server is the only authority on whether a token is actually valid, so this
 * must never be used to make an authorization decision.
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
    };
  } catch {
    return null;
  }
}

/** Whether a decoded session's token has passed its expiry. */
export function isExpired(session: DecodedSession, nowMs = Date.now()): boolean {
  return session.expiresAtUtc * 1000 <= nowMs;
}
