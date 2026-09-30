/** Test-only helper: builds a syntactically valid JWT carrying the given payload (unsigned). */
export function buildFakeJwt(payload: Record<string, unknown>): string {
  const encode = (value: unknown): string =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

  return `${encode({ alg: 'none', typ: 'JWT' })}.${encode(payload)}.signature`;
}
