/**
 * Question load (NFR-1: question load under 500 ms p95 at 2x peak).
 *
 * Each virtual user signs in as its own test candidate and opens that candidate's attempt once.
 * Each iteration then reads the attempt's paper, GET /v1/me/attempts/{attemptId}, which returns
 * every question and option the candidate sees while the attempt is open.
 *
 * Why this endpoint: the candidate client loads the paper with this call (candidate-api.service
 * getAttempt). Section 13 of the requirements lists /attempts/{id}/paper, which the API does not
 * expose, so the script follows the client, not the outline.
 *
 * Run it with the settings described in tests/load/README.md.
 */
import http from 'k6/http';
import { check } from 'k6';
import {
  arrivalRateScenario,
  assertPoolCovers,
  maxVUsFor,
  planFromEnv,
  readRunConfig,
} from './lib/plan.js';
import { openSession, requestParams } from './lib/candidate.js';

const run = readRunConfig(__ENV);
const plan = planFromEnv(__ENV);
assertPoolCovers(run.accountCount, maxVUsFor(plan.questionRate));

export const options = {
  scenarios: {
    question_load: arrivalRateScenario(plan.questionRate, plan.rampSeconds, plan.holdSeconds),
  },
  thresholds: {
    // NFR-1. Only the paper read is judged; sign-in and attempt start are tagged `prepare` and left out.
    'http_req_duration{endpoint:question_load}': ['p(95)<500'],
    'http_req_failed{endpoint:question_load}': ['rate<0.01'],
    // If the server cannot keep up, k6 drops arrivals instead of slowing down. A dropped arrival
    // means the offered load was not delivered, so the run is invalid, not a pass.
    dropped_iterations: ['count==0'],
    // Catches a failed sign-in or attempt start, which would otherwise look like a quiet run.
    checks: ['rate>0.99'],
  },
};

// Module state persists for the life of a virtual user, so the sign-in and attempt start happen once per VU.
let session = null;

export default function () {
  if (session === null) {
    session = openSession(run, __VU - 1);
  }

  const res = http.get(
    `${run.baseUrl}/v1/me/attempts/${session.attemptId}`,
    requestParams(session.token, 'question_load'),
  );
  check(res, { 'paper loaded (200)': (r) => r.status === 200 });
}
