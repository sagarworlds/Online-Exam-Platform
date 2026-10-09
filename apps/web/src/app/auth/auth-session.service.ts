import { Injectable, computed, signal } from '@angular/core';
import { DecodedSession, decodeJwt, isExpired } from './jwt';

export const AUTH_TOKEN_STORAGE_KEY = 'exam-platform.auth-token';

/**
 * Holds the signed-in user's session, decoded from the JWT the API issues.
 * The raw token is kept in localStorage — the simplest option for this
 * SPA-only client this slice; revisit in favour of an httpOnly cookie if the
 * XSS surface grows before real production traffic (see ADR 0001 follow-ups).
 */
@Injectable({ providedIn: 'root' })
export class AuthSessionService {
  private readonly sessionSignal = signal<DecodedSession | null>(this.readStoredSession());

  readonly session = this.sessionSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.sessionSignal() !== null);

  /** Whether the token carries the permission. Decides what the UI offers; the API re-checks every call. */
  hasPermission(code: string): boolean {
    return this.sessionSignal()?.permissions.includes(code) ?? false;
  }

  /** Whether the token carries at least one of the permissions. */
  hasAnyPermission(codes: readonly string[]): boolean {
    return codes.some((code) => this.hasPermission(code));
  }

  /** The raw bearer token to attach to authenticated requests, if signed in. */
  get accessToken(): string | null {
    return this.sessionSignal() === null ? null : localStorage.getItem(AUTH_TOKEN_STORAGE_KEY);
  }

  /** Decodes and stores a freshly issued access token. */
  login(accessToken: string): void {
    const session = decodeJwt(accessToken);
    if (session === null || isExpired(session)) {
      throw new Error('Received an invalid or already-expired access token.');
    }

    localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, accessToken);
    this.sessionSignal.set(session);
  }

  /** Clears the current session. */
  logout(): void {
    localStorage.removeItem(AUTH_TOKEN_STORAGE_KEY);
    this.sessionSignal.set(null);
  }

  private readStoredSession(): DecodedSession | null {
    const token = localStorage.getItem(AUTH_TOKEN_STORAGE_KEY);
    if (token === null) {
      return null;
    }

    const session = decodeJwt(token);
    if (session === null || isExpired(session)) {
      localStorage.removeItem(AUTH_TOKEN_STORAGE_KEY);
      return null;
    }

    return session;
  }
}
