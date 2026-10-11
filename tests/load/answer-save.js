/**
 * Answer save (NFR-1: answer save under 300 ms p95 at 2x peak).
 *
 * Each virtual user signs in as its own test candidate and starts that candidate's attempt. Each
 * iteration then saves one answer, PUT /v1/me/attempts/{attemptId}/answers/{questionId} with
 * {optionId}, which is the autosave the candidate client sends as they click.
 *
 * Only single-choice questions are saved. See `singleChoiceAnswers` in lib/plan.js for why the
 * other kinds are left out.
 *
 * Run it with the settings described in tests/load/README.md.
 */
import http from 'k6/http';
import { check } from 'k6';
import {
  arrivalRateScenario,
  assertPoolCovers,
  maxVUsFor,
  pickAnswer,
  planFromEnv,
  readRunConfig,
  singleChoiceAnswers,
} from './lib/plan.js';
import { openSession, requestParams } from './lib/candidate.js';

const run = readRunConfig(__ENV);
const plan = planFromEnv(__ENV);
assertPoolCovers(run.accountCount, maxVUsFor(plan.saveRate));

export const options = {
  scenarios: {
    answer_save: arrivalRateScenario(plan.saveRate, plan.rampSeconds, plan.holdSeconds),
  },
  thresholds: {
    // NFR-1. Only the autosave is judged; sign-in and attempt start are tagged `prepare` and left out.
    'http_req_duration{endpoint:answer_save}': ['p(95)<300'],
    'http_req_failed{endpoint:answer_save}': ['rate<0.01'],
    // A dropped arrival means the offered load was not delivered, so the run is invalid, not a pass.
    dropped_iterations: ['count==0'],
    // Catches a failed sign-in or attempt start, which would otherwise look like a quiet run.
    checks: ['rate>0.99'],
  },
};

// Module state persists for the life of a virtual user: the session, its answerable questions, and a save counter.
let session = null;
let answers = [];
let step = 0;

export default function () {
  if (session === null) {
    session = openSession(run, __VU - 1);
    answers = singleChoiceAnswers(session.attempt);
  }

  const { questionId, optionId } = pickAnswer(answers, step);
  step += 1;

  const res = http.put(
    `${run.baseUrl}/v1/me/attempts/${session.attemptId}/answers/${questionId}`,
    JSON.stringify({ optionId }),
    requestParams(session.token, 'answer_save'),
  );
  check(res, { 'answer saved (204)': (r) => r.status === 204 });
}
