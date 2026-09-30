/**
 * Why the API refused an existing session: the ProblemDetails `title` it sends
 * with a 401 when the token's session is no longer valid (the backend's
 * SessionValidationResult error codes). The auth interceptor forwards one of
 * these to /login as `?reason=` so the login page can explain the sign-out.
 */
export const SESSION_END_REASONS = [
  'session_unknown',
  'session_superseded',
  'session_revoked',
  'session_expired',
  'account_locked',
] as const;

/** One of the {@link SESSION_END_REASONS} codes. */
export type SessionEndReason = (typeof SESSION_END_REASONS)[number];

/**
 * Whether a value is a known {@link SessionEndReason}.
 *
 * @param value Anything read from a response body or a query parameter.
 * @returns True only for one of the exact codes the API documents.
 */
export function isSessionEndReason(value: unknown): value is SessionEndReason {
  return typeof value === 'string' && (SESSION_END_REASONS as readonly string[]).includes(value);
}
