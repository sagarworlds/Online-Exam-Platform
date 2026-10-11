# Load tests (NFR-1, NFR-2)

Two k6 scripts, one per path the performance requirement names. Both run open-model arrival
rates at 2x the expected peak. They are manual: they need a staging environment and must not run
on pull requests.

| Script | Path | Request | Threshold (p95) |
|---|---|---|---|
| `question-load.js` | Loading a question: the candidate's paper at an attempt | `GET /v1/me/attempts/{attemptId}` | under 500 ms |
| `answer-save.js` | Saving an answer | `PUT /v1/me/attempts/{attemptId}/answers/{questionId}` | under 300 ms |

Both scripts also require: under 1% failed requests on the measured endpoint, zero dropped
arrivals, and more than 99% of checks passing. A run that misses any of these fails.

The question path follows the candidate client (`getAttempt` in the web app). Section 13 of the
requirements lists `/attempts/{id}/paper`, which the API does not expose.

## The 2x peak workload

The requirements do not state an expected peak. These scripts take section 9's tier-1 ceiling of
10,000 concurrent test-takers as the expected peak, so the 2x cohort is 20,000 candidates.

| Setting | Default | Basis |
|---|---|---|
| `LOAD_PEAK_CONCURRENT_CANDIDATES` | 10000 | Section 9, tier 1 ceiling |
| `LOAD_PEAK_MULTIPLIER` | 2 | NFR-2 |
| `LOAD_SAVE_INTERVAL_SECONDS` | 20 | Assumed: one candidate saves an answer about every 20 s |
| `LOAD_ENTRY_WINDOW_SECONDS` | 120 | Assumed: the whole cohort opens its paper within 2 minutes |
| `LOAD_RAMP_SECONDS` | 120 | Ramp from idle, so the first sign-ins do not arrive as one burst |
| `LOAD_HOLD_SECONDS` | 600 | Time held at the target rate; thresholds cover the whole run, ramp included |

At the defaults that gives **1,000 answer saves per second** (20,000 / 20) and **167 paper reads per
second** (20,000 / 120). Set `LOAD_SAVE_RPS` or `LOAD_QUESTION_RPS` to override the derived rate, as
a smoke run does.

Each virtual user signs in as its own candidate, one account per VU. The pool therefore needs at
least as many accounts as the VU ceiling, which is the rate itself (one second of latency per
arrival). The script refuses to start otherwise.

## Staging prerequisites

1. **Exam.** Publish a dedicated load-test exam, with a duration longer than ramp plus hold plus a
   margin. Saves to an attempt that is no longer in progress are refused, so a short exam makes the
   late part of the run measure refusals. Use a fresh exam for each run.
2. **Candidates.** Create `LOAD_TEST_ACCOUNT_COUNT` accounts that match `LOAD_TEST_EMAIL_PATTERN`
   (`{n}` is replaced by 1, 2, 3, ...). They must share one password and be enrolled in the exam,
   which the start call checks along with the exam being published. They must not need a second
   factor: the Candidate role has `RequiresTwoFactor` set to false.
3. **Rate limits.** The API limits by client IP. The defaults would turn a load test into a test of
   the limiter:
   - `RateLimiting:Global` allows 60 requests per 60 s per IP. Raise `PermitLimit` (environment form
     `RateLimiting__Global__PermitLimit`) to cover the offered load, about 70,000 per window at the
     defaults (1,167 requests per second, times 60).
   - `Identity:RateLimits:PasswordLogin` allows 10 sign-ins per 300 s per IP. Raise `PermitLimit` to at
     least the account count within the first window.

   Raise these for the run only, from a generator IP the environment accepts, and restore them after.
   Without that, the run measures 429 responses. Those are fast, so p95 can look good. The
   `http_req_failed` threshold is what catches it.
4. **Target.** Staging only. Never point these scripts at production.

## Running

Settings are read from the environment. Keep them in a file outside the repository, because the
password is a credential.

| Variable | Meaning |
|---|---|
| `LOAD_BASE_URL` | API origin, for example `https://<staging-api-host>`, with no path |
| `LOAD_EXAM_ID` | GUID of the load-test exam |
| `LOAD_TEST_EMAIL_PATTERN` | Email pattern for the pool, containing `{n}`, for example `candidate-{n}@<staging-test-domain>` |
| `LOAD_TEST_PASSWORD` | Password shared by the pool |
| `LOAD_TEST_ACCOUNT_COUNT` | Number of accounts in the pool |

Against staging, with Docker and the pinned k6 image:

```sh
docker run --rm --env-file /path/outside/the/repo/load.env \
  -v "$PWD/tests/load:/scripts:ro" grafana/k6:1.0.0 run /scripts/answer-save.js

docker run --rm --env-file /path/outside/the/repo/load.env \
  -v "$PWD/tests/load:/scripts:ro" grafana/k6:1.0.0 run /scripts/question-load.js
```

A smoke run checks the setup without the full load. Add these lines to the env file for one run, and
remove them afterwards. The pool must still cover the rate, so 5 accounts is enough for 2 per second.

```sh
LOAD_SAVE_RPS=2
LOAD_QUESTION_RPS=2
LOAD_TEST_ACCOUNT_COUNT=5
LOAD_RAMP_SECONDS=10
LOAD_HOLD_SECONDS=30
```

Through GitHub, run the **Load test (manual)** workflow (`workflow_dispatch`) against the `staging`
environment. It takes the origin, the exam id, the scenario and the pool size as inputs. The email
pattern and password come from that environment's secrets, `LOAD_TEST_EMAIL_PATTERN` and
`LOAD_TEST_PASSWORD`.

Helper unit tests, which need Node 22 or later and no install:

```sh
cd tests/load && npm test
```

k6 exits with code 99 when a threshold fails, which fails the workflow. Read the p95 for
`answer_save` and `question_load` in the summary, and check that `dropped_iterations` is zero. A
non-zero drop count means the offered load was not delivered, so the run does not count as a pass.

## Limits of this test

- The start-time spike (`POST .../attempts`) and the last-minute submit surge are not measured. The
  two paths named by NFR-1 are the ones covered. Both are follow-ups for the same milestone.
- Each virtual user reads and writes its own attempt. Against a cohort of distinct attempts, the same
  request rate touches more rows and may defeat any cache keyed by attempt. Reads may look cheaper than
  they are, and writes may look more contended than they are.
- Sign-in and attempt start are not timed here, so the run does not show the time to first question.
- The k6 generator runs in a data centre, not on candidate networks. Low-bandwidth mode, picture fetches,
  heartbeats and status polling are not in these scripts.
