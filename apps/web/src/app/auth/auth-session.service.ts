import { Injectable, computed, signal } from '@angular/core';
import { DecodedSession, decodeJwt, isExpired } from './jwt';

export const AUTH_TOKEN_STORAGE_KEY = 'exam-platform.auth-token';

/**
 * Holds the signed-in user's session, decoded from the JWT the API issues.
 * The raw token is kept in localStorage — the simplest option for this
 * SPA-only client this slice; revisit in favour of an httpOnly cookie if the
 * XSS surface grows before real production traffic (see ADR 0001 follow-ups).
 *
 * The browser may refuse the store (blocked site data, a private window, a full quota), so every
 * access is guarded: a store that cannot be read means no session, and one that cannot be written
 * only means the session is not kept past this tab — it still works until then.
 */
@Injectable({ providedIn: 'root' })
export class AuthSessionService {
  // Declared before the session signal, whose initializer reads the store and sets this.
  /** The raw token held in memory too, so the session keeps working when the store will not take it. */
  private memoryToken: string | null = null;
  /** The store refused the token at sign-in, so whatever it holds is not this session's. */
  private storeRefusedToken = false;

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
    if (this.sessionSignal() === null) {
      return null;
    }
    if (this.storeRefusedToken) {
      return this.memoryToken;
    }

    try {
      return localStorage.getItem(AUTH_TOKEN_STORAGE_KEY);
    } catch {
      // A store that stopped answering: the copy kept in memory is the same token.
      return this.memoryToken;
    }
  }

  /** Decodes and stores a freshly issued access token. */
  login(accessToken: string): void {
    const session = decodeJwt(accessToken);
    if (session === null || isExpired(session)) {
      throw new Error('Received an invalid or already-expired access token.');
    }

    this.memoryToken = accessToken;
    try {
      localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, accessToken);
      this.storeRefusedToken = false;
    } catch {
      // A blocked or full store only means the user signs in again after closing the tab; this session carries on from memory.
      this.storeRefusedToken = true;
    }
    this.sessionSignal.set(session);
  }

  /** Clears the current session. */
  logout(): void {
    this.memoryToken = null;
    this.storeRefusedToken = false;
    this.forgetStoredToken();
    this.sessionSignal.set(null);
  }

  private readStoredSession(): DecodedSession | null {
    let token: string | null;
    try {
      token = localStorage.getItem(AUTH_TOKEN_STORAGE_KEY);
    } catch {
      // A store that cannot be read has no session to resume; the user signs in.
      return null;
    }
    if (token === null) {
      return null;
    }

    const session = decodeJwt(token);
    if (session === null || isExpired(session)) {
      this.forgetStoredToken();
      return null;
    }

    this.memoryToken = token;
    return session;
  }

  /** Removes the stored token. A store that will not let it go is left as it is: there is nothing more to do from here. */
  private forgetStoredToken(): void {
    try {
      localStorage.removeItem(AUTH_TOKEN_STORAGE_KEY);
    } catch {
      // The session in memory is cleared by the caller either way.
    }
  }
}
