/**
 * Pure configuration and workload helpers for the load scripts (NFR-1, NFR-2).
 *
 * Nothing in this file imports a k6 module, so `node --test` can exercise it in CI and on a
 * laptop. The k6 scripts import it too, which makes this the single place where the 2x peak
 * workload is derived from its inputs.
 */

/**
 * Section 9, tier 1: "up to 10k" concurrent test-takers at launch. The requirements do not name
 * an expected peak, so the launch ceiling is used as the expected peak. Confirm before relying on it.
 */
export const DEFAULT_PEAK_CONCURRENT_CANDIDATES = 10000;

/** NFR-2: load-test at 2x the expected peak. */
export const DEFAULT_PEAK_MULTIPLIER = 2;

/**
 * Average seconds between one candidate's answer saves. Assumed, not taken from the requirements:
 * a candidate changes an answer about every 20 seconds over a multi-hour paper.
 */
export const DEFAULT_SAVE_INTERVAL_SECONDS = 20;

/**
 * Seconds in which the whole cohort opens its paper. Assumed. It is shorter than the staggered
 * entry windows section 9 recommends, so the run is harsher than a planned exam start.
 */
export const DEFAULT_ENTRY_WINDOW_SECONDS = 120;

/** Ramp from idle to the target rate, so the first logins do not arrive as one burst. */
export const DEFAULT_RAMP_SECONDS = 120;

/** Time spent at the target rate. Thresholds are judged over the whole run, ramp included. */
export const DEFAULT_HOLD_SECONDS = 600;

/**
 * How long one iteration may take before k6 counts the arrival as dropped: one second of latency
 * per in-flight request. Sized so the VU pool does not become the bottleneck before the server does.
 */
export const VU_BUDGET_SECONDS = 1;

const GUID_PATTERN = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

/**
 * Reads a positive whole number from an environment value.
 *
 * An unset or blank value takes the fallback. Anything else that is not a positive whole number
 * throws, so a mistyped setting stops the run instead of quietly testing a different load.
 *
 * @param {string} name The environment variable name, used in the error message.
 * @param {string | number | undefined} raw The value as read from the environment.
 * @param {number} fallback The value to use when the variable is unset or blank.
 * @returns {number} The parsed value, or the fallback.
 * @throws {Error} When the value is set but is not a positive whole number.
 */
export function positiveInt(name, raw, fallback) {
  if (raw === undefined || raw === null || String(raw).trim() === '') {
    return fallback;
  }
  const text = String(raw).trim();
  const value = Number(text);
  if (!/^\d+$/.test(text) || !Number.isSafeInteger(value) || value <= 0) {
    throw new Error(`${name} must be a positive whole number, got "${text}"`);
  }
  return value;
}

/**
 * Derives the workload for a run from the peak inputs. Explicit rates (LOAD_SAVE_RPS,
 * LOAD_QUESTION_RPS) override the derivation, which is how a smoke run is kept small.
 *
 * Derivation at the defaults: 10,000 peak candidates x 2 = 20,000 candidates. Each saves an answer
 * every 20 s, so 1,000 saves per second. The cohort opens its paper within 120 s, so about 167
 * paper reads per second.
 *
 * @param {Record<string, string | undefined>} env The environment, normally k6's `__ENV`.
 * @returns {{cohort: number, saveRate: number, questionRate: number, rampSeconds: number, holdSeconds: number}}
 *   The cohort size, the two arrival rates in requests per second, and the ramp and hold durations.
 */
export function planFromEnv(env) {
  const peak = positiveInt(
    'LOAD_PEAK_CONCURRENT_CANDIDATES',
    env.LOAD_PEAK_CONCURRENT_CANDIDATES,
    DEFAULT_PEAK_CONCURRENT_CANDIDATES,
  );
  const multiplier = positiveInt('LOAD_PEAK_MULTIPLIER', env.LOAD_PEAK_MULTIPLIER, DEFAULT_PEAK_MULTIPLIER);
  const cohort = peak * multiplier;
  const saveInterval = positiveInt(
    'LOAD_SAVE_INTERVAL_SECONDS',
    env.LOAD_SAVE_INTERVAL_SECONDS,
    DEFAULT_SAVE_INTERVAL_SECONDS,
  );
  const entryWindow = positiveInt(
    'LOAD_ENTRY_WINDOW_SECONDS',
    env.LOAD_ENTRY_WINDOW_SECONDS,
    DEFAULT_ENTRY_WINDOW_SECONDS,
  );
  return {
    cohort,
    saveRate: positiveInt('LOAD_SAVE_RPS', env.LOAD_SAVE_RPS, Math.ceil(cohort / saveInterval)),
    questionRate: positiveInt('LOAD_QUESTION_RPS', env.LOAD_QUESTION_RPS, Math.ceil(cohort / entryWindow)),
    rampSeconds: positiveInt('LOAD_RAMP_SECONDS', env.LOAD_RAMP_SECONDS, DEFAULT_RAMP_SECONDS),
    holdSeconds: positiveInt('LOAD_HOLD_SECONDS', env.LOAD_HOLD_SECONDS, DEFAULT_HOLD_SECONDS),
  };
}

/**
 * The most virtual users a rate can need: one per request that may be in flight at once.
 *
 * @param {number} rate Iterations per second.
 * @returns {number} The VU ceiling for that rate.
 */
export function maxVUsFor(rate) {
  return rate * VU_BUDGET_SECONDS;
}

/**
 * Builds an open-model arrival-rate scenario.
 *
 * k6 starts iterations at the target rate whether or not earlier ones have finished, as real
 * candidates do. A slow server therefore shows up as latency and dropped iterations. A closed
 * model (a fixed number of looping VUs) would slow its own offered load when the server slowed,
 * and so would understate latency at 2x.
 *
 * @param {number} rate The target iterations per second.
 * @param {number} rampSeconds Seconds to ramp from 1 per second to the target.
 * @param {number} holdSeconds Seconds to hold the target.
 * @returns {object} A k6 `ramping-arrival-rate` scenario definition.
 */
export function arrivalRateScenario(rate, rampSeconds, holdSeconds) {
  return {
    executor: 'ramping-arrival-rate',
    startRate: 1,
    timeUnit: '1s',
    preAllocatedVUs: Math.max(1, Math.ceil(rate / 10)),
    maxVUs: maxVUsFor(rate),
    stages: [
      { target: rate, duration: `${rampSeconds}s` },
      { target: rate, duration: `${holdSeconds}s` },
    ],
  };
}

/**
 * Reads the settings that point a run at a staging environment. The candidate password is
 * validated but never echoed in an error message.
 *
 * @param {Record<string, string | undefined>} env The environment, normally k6's `__ENV`.
 * @returns {{baseUrl: string, examId: string, emailPattern: string, password: string, accountCount: number}}
 *   The API origin without a trailing slash, the exam the test candidates are enrolled in, the
 *   email pattern for the account pool, the shared password, and the number of accounts.
 * @throws {Error} When a setting is missing or malformed.
 */
export function readRunConfig(env) {
  const baseUrl = requiredText(env, 'LOAD_BASE_URL').trim().replace(/\/+$/, '');
  if (!/^https?:\/\/[^/]+/.test(baseUrl)) {
    throw new Error('LOAD_BASE_URL must be an http:// or https:// origin, for example https://<staging-api-host>');
  }

  const examId = requiredText(env, 'LOAD_EXAM_ID').trim();
  if (!GUID_PATTERN.test(examId)) {
    throw new Error('LOAD_EXAM_ID must be the exam GUID');
  }

  const emailPattern = requiredText(env, 'LOAD_TEST_EMAIL_PATTERN').trim();
  const password = requiredText(env, 'LOAD_TEST_PASSWORD');
  const accountCount = positiveInt(
    'LOAD_TEST_ACCOUNT_COUNT',
    requiredText(env, 'LOAD_TEST_ACCOUNT_COUNT'),
    0,
  );

  // Without {n} every account would have the same email, so the pool could not hold more than one candidate.
  if (accountCount > 1 && emailPattern.indexOf('{n}') === -1) {
    throw new Error('LOAD_TEST_EMAIL_PATTERN must contain {n} when LOAD_TEST_ACCOUNT_COUNT is more than 1');
  }

  return { baseUrl, examId, emailPattern, password, accountCount };
}

/**
 * The email address of one account in the pool.
 *
 * @param {string} pattern The email pattern with `{n}` where the account number goes.
 * @param {number} index Zero-based account index.
 * @returns {string} The email address for that account.
 */
export function accountEmail(pattern, index) {
  return pattern.split('{n}').join(String(index + 1));
}

/**
 * Checks that the pool has an account for every VU that may run.
 *
 * Each VU signs in as its own account. A candidate login supersedes the account's active session
 * (User.StartNewSession), so two VUs on one account would revoke each other's tokens and the
 * run would measure 401s. The pool must therefore be at least as large as the VU ceiling.
 *
 * @param {number} accountCount Accounts in the pool.
 * @param {number} maxVUs The VU ceiling for the scenario.
 * @throws {Error} When the pool is smaller than the VU ceiling.
 */
export function assertPoolCovers(accountCount, maxVUs) {
  if (accountCount < maxVUs) {
    throw new Error(
      `LOAD_TEST_ACCOUNT_COUNT (${accountCount}) must be at least the VU ceiling (${maxVUs}); one account per VU`,
    );
  }
}

/**
 * The questions a save run can answer with one click: single-choice questions with options.
 *
 * Multiple-answer and typed questions use a different request body, so they are left out rather
 * than sent as a shape the run was not asked to measure. While sections are locked, only the
 * active section accepts answers, and the server refuses the rest, so those are left out too.
 *
 * @param {object} attempt An AttemptDto as returned by POST /v1/me/exams/{examId}/attempts.
 * @returns {{questionId: string, optionIds: string[]}[]} The answerable questions with their option ids.
 */
export function singleChoiceAnswers(attempt) {
  const lockedToActive = attempt.sectionLockEnabled === true;
  const activeSectionId = attempt.activeSectionId || null;
  const answers = [];
  for (const section of attempt.sections || []) {
    if (lockedToActive && section.id !== activeSectionId) {
      continue;
    }
    for (const question of section.questions || []) {
      if (question.allowsMultiple === true || question.isTextAnswer === true) {
        continue;
      }
      const options = question.options || [];
      if (options.length === 0) {
        continue;
      }
      answers.push({ questionId: question.id, optionIds: options.map((option) => option.id) });
    }
  }
  return answers;
}

/**
 * Picks the answer for one save. Questions rotate on each step, and within a question the option
 * changes on each pass, so consecutive saves are real changes to the stored answer.
 *
 * @param {{questionId: string, optionIds: string[]}[]} answers From {@link singleChoiceAnswers}.
 * @param {number} step The save number for this virtual user, from 0.
 * @returns {{questionId: string, optionId: string}} The question and option to save.
 * @throws {Error} When there is nothing to answer.
 */
export function pickAnswer(answers, step) {
  if (answers.length === 0) {
    throw new Error('the attempt has no single-choice question to save an answer to');
  }
  const question = answers[step % answers.length];
  const pass = Math.floor(step / answers.length);
  return {
    questionId: question.questionId,
    optionId: question.optionIds[pass % question.optionIds.length],
  };
}

/**
 * Returns a required, non-blank environment value, or throws naming the variable.
 *
 * @param {Record<string, string | undefined>} env The environment.
 * @param {string} name The variable name.
 * @returns {string} The value, unchanged.
 * @throws {Error} When the variable is unset or blank.
 */
function requiredText(env, name) {
  const value = env[name];
  if (value === undefined || String(value).trim() === '') {
    throw new Error(`${name} is required`);
  }
  return String(value);
}
