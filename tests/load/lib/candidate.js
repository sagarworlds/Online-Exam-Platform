/**
 * Candidate sign-in and attempt start for the k6 scripts.
 *
 * These are the same calls the candidate client makes before the exam page loads
 * (POST /v1/auth/login, then POST /v1/me/exams/{examId}/attempts). Each virtual user runs them
 * once, on its first iteration, and keeps the token and attempt id. Those requests are tagged
 * `prepare` and sit outside the thresholds, so the numbers reflect the measured paths and not
 * the sign-in.
 */
import http from 'k6/http';
import { accountEmail } from './plan.js';

const JSON_HEADERS = { 'Content-Type': 'application/json', Accept: 'application/json' };

/**
 * Signs one test candidate in and returns the bearer token.
 *
 * Test candidates must not need a second factor (RbacCatalog: the Candidate role has
 * RequiresTwoFactor false), so a missing token is a setup error and not something to retry.
 *
 * @param {string} baseUrl The API origin, without a trailing slash.
 * @param {string} email The candidate's email address.
 * @param {string} password The candidate's password.
 * @returns {string} The access token.
 * @throws {Error} When the sign-in is refused or returns no token. The message carries the HTTP
 *   status only, never the email or password.
 */
export function signIn(baseUrl, email, password) {
  const res = http.post(
    `${baseUrl}/v1/auth/login`,
    JSON.stringify({ email, password }),
    { headers: JSON_HEADERS, tags: { endpoint: 'prepare' } },
  );
  if (res.status === 429) {
    throw new Error('sign-in returned HTTP 429: the per-IP login limit is in the way; see tests/load/README.md');
  }
  if (res.status !== 200) {
    throw new Error(`sign-in returned HTTP ${res.status}`);
  }
  const body = res.json();
  if (!body || !body.accessToken) {
    throw new Error('sign-in returned no access token; test candidates must not need a second factor');
  }
  return body.accessToken;
}

/**
 * Starts the candidate's attempt at the load-test exam, or resumes the one they already have.
 *
 * `instructionsAcknowledged` is sent as true, as the instructions page does, because a new attempt
 * is refused without it.
 *
 * @param {string} baseUrl The API origin, without a trailing slash.
 * @param {string} token The candidate's bearer token.
 * @param {string} examId The load-test exam.
 * @returns {object} The AttemptDto, including its sections and questions.
 * @throws {Error} When the attempt cannot be started. The exam must be published and the candidate enrolled.
 */
export function startAttempt(baseUrl, token, examId) {
  const res = http.post(
    `${baseUrl}/v1/me/exams/${examId}/attempts`,
    JSON.stringify({ instructionsAcknowledged: true }),
    { headers: authHeaders(token), tags: { endpoint: 'prepare' } },
  );
  if (res.status !== 200) {
    throw new Error(`starting the attempt returned HTTP ${res.status}`);
  }
  return res.json();
}

/**
 * Signs in the account at `index` in the pool and starts its attempt.
 *
 * @param {{baseUrl: string, examId: string, emailPattern: string, password: string}} run The run settings.
 * @param {number} index Zero-based account index, one per virtual user.
 * @returns {{token: string, attemptId: string, attempt: object}} What a virtual user needs to keep.
 */
export function openSession(run, index) {
  const token = signIn(run.baseUrl, accountEmail(run.emailPattern, index), run.password);
  const attempt = startAttempt(run.baseUrl, token, run.examId);
  return { token, attemptId: attempt.id, attempt };
}

/**
 * Headers for an authenticated request.
 *
 * @param {string} token The bearer token.
 * @returns {Record<string, string>} The headers.
 */
export function authHeaders(token) {
  return {
    'Content-Type': 'application/json',
    Accept: 'application/json',
    Authorization: `Bearer ${token}`,
  };
}

/**
 * Request parameters for a measured call, tagged so the thresholds can select it.
 *
 * @param {string} token The bearer token.
 * @param {string} endpoint The tag the thresholds select on, for example `question_load`.
 * @returns {{headers: Record<string, string>, tags: {endpoint: string}}} The k6 request parameters.
 */
export function requestParams(token, endpoint) {
  return { headers: authHeaders(token), tags: { endpoint } };
}
