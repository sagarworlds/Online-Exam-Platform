# Pilot go/no-go checklist

**For:** the pilot exam, [#91](https://github.com/sagarworlds/Online-Exam-Platform/issues/91), the last item of M8 in the requirements ("load test at 2x passes NFR-1/NFR-4; security test clean; accessibility audit passed; pilot exam completed"), and go-live, [#93](https://github.com/sagarworlds/Online-Exam-Platform/issues/93).
**Scope decision (D2, 2026-10-15):** the pilot uses **adult candidates only**. Minors come in after counsel's opinion on them and the remaining guardian-consent work.
**Status of this list:** as of 2026-10-15. Re-check each row's source before relying on it. This is an engineering checklist, not legal advice.

## What adults only does, and does not, change

- **It removes the wait for counsel on minors.** The pilot no longer needs the opinion on monitoring children (counsel brief, A and B), the minors' part of [#255](https://github.com/sagarworlds/Online-Exam-Platform/issues/255), or the guardian-consent notice.
- **It does not remove the wait for counsel on the rest.** Adult candidates' data is personal data. `docs/compliance/nfr-12-exception.md` says counsel must confirm the data-location exception **before any real candidate data is stored**, and the consent notices (#269) are counsel's to review. Both are in the first block below.
- **A candidate under 18 can still register.** If one appears on the pilot roster, the platform stops them at the start of an attempt until a guardian has confirmed the link (PR #267). That is a safety net and not the plan: the school confirms that every candidate on the pilot roster is an adult.

## How to read the status

*Done* is checked in the repository or on GitHub. *Waiting on you* names the decision or action in `docs/owner-decisions.md`. *Waiting on counsel* needs the brief sent first (A12). *Engineering* is work that can start without an answer.

## 1. Legal and privacy: a "no" on any row stops the pilot

| Gate | Why | Source | Status |
|---|---|---|---|
| Counsel confirms the data-location exception in writing | The exception is "not reviewed by counsel" and says it must be confirmed before real candidate data is stored | `nfr-12-exception.md`, brief questions 14 and 15 | Waiting on counsel. The brief has not been sent (A12) |
| Consent notices are in the ledger with real addresses | The ledger points at placeholder addresses today | [#269](https://github.com/sagarworlds/Online-Exam-Platform/issues/269), brief question 16 | Waiting on counsel and on D4 |
| Consent is required before an attempt | Nothing calls the consent service today, so a candidate can start without consenting (data map G3) | #255 (the consent gate), data map G3 | Engineering, after the notices exist |
| A grievance officer is named and shown on the privacy page | FR-48 and the notices need one. The page has placeholders | D4, A6, [#80](https://github.com/sagarworlds/Online-Exam-Platform/issues/80) | Waiting on you |
| The privacy page matches what the platform does | It makes retention and processor statements the data map does not back (OP11) | data map OP11 and G14 | Waiting on D3, D4 and counsel |
| Data-processing agreements are confirmed with Render, Neon and Brevo | They are conditions of the exception | `nfr-12-exception.md`, A5 | Waiting on you |
| The pilot roster is adults only, confirmed by the school | D2 | This file | Waiting on the school, at roster time |
| Retention periods are agreed, and the privacy notice states them | The privacy notice must say how long data is kept | D3, `retention-proposal.md`, [#79](https://github.com/sagarworlds/Online-Exam-Platform/issues/79) | Waiting on you (A13) and counsel. The privacy notice must state the periods before the pilot. Whether the job (#79) must exist before the pilot, or can follow it, is for the owner to decide with counsel |

## 2. Security (M8: security test clean)

| Gate | Source | Status |
|---|---|---|
| The key that protects the question-content keys exists on Render and is in a password manager, and the key-ring change is deployed | A1, [#86](https://github.com/sagarworlds/Online-Exam-Platform/issues/86), `docs/adr/0003-key-ring-protection.md` | Merged in [#274](https://github.com/sagarworlds/Online-Exam-Platform/pull/274). A1 is done, as reported by the owner on 2026-10-16; engineering cannot see Render, so the key's presence is not checked here |
| Static-site security headers are set, and the weekly header check is green | A2, A3, `nfr-5-security-checks.md` | Waiting on you |
| A penetration test has run and its findings are fixed or accepted in writing | A4 | Waiting on you |
| The WAF decision is recorded | D15, `waf-decision.md`, brief question 17 | Waiting on you and counsel |
| The paper-before-start exception is recorded | D16 | Done: `nfr-5-paper-before-start-exception.md` |
| The `katex` advisory is accepted | D17 | Done: `nfr-5-security-checks.md` |

## 3. Reliability and performance (M8: load test at 2x passes)

| Gate | Source | Status |
|---|---|---|
| A staging environment exists | D11, A14, `docs/runbooks/staging.md` | Waiting on you |
| The load test passes at 2x: question load under 500 ms and answer save under 300 ms at p95, under 1% failed, no dropped arrivals | [#84](https://github.com/sagarworlds/Online-Exam-Platform/issues/84), `tests/load/README.md` | Waiting on staging. The test accounts are created by `--create-load-test-candidates` and `--enrol-load-test-candidates` ([ADR 0006](../adr/0006-load-test-candidate-pool.md)) |
| Answer saves tolerate a delayed older save (NFR-4) | D20, `docs/adr/0004-answer-save-sequence-numbers.md` | Merged in [#274](https://github.com/sagarworlds/Online-Exam-Platform/pull/274) |
| A restore drill has been run, and the recovery point and recovery time are recorded | [#85](https://github.com/sagarworlds/Online-Exam-Platform/issues/85), D13, `docs/runbooks/staging.md` | Waiting on staging and D13 |
| Logs are kept for 180 days in the chosen store | [#89](https://github.com/sagarworlds/Online-Exam-Platform/issues/89), D14, A15 | Waiting on counsel (question 15) and you |
| Someone watches the exam window, and knows where to look | [#93](https://github.com/sagarworlds/Online-Exam-Platform/issues/93), `docs/observability.md` (no alerts are defined) | Waiting on you |
| No deployment happens during the exam window | NFR-3 asks for a change freeze before big exams. No process exists | Waiting on you |
| E-mail capacity covers the pilot (sign-in codes, invitations, results) | D11, `docs/deploy-render.md` (the free Brevo plan is about 300 a day) | Waiting on you |

## 4. Accessibility and compatibility (M8: accessibility audit passed)

| Gate | Source | Status |
|---|---|---|
| The exam time limit is recorded as essential | D18, audit finding A-06 | Done: `accessibility-audit.md` |
| The manual pass with a screen reader and a keyboard-only session is done, and its findings are fixed | D19, A10, `accessibility-manual-checklist.md`, [#87](https://github.com/sagarworlds/Online-Exam-Platform/issues/87) | Waiting on you and volunteers |
| The exam runs on iOS Safari and Android Chrome | D19, A10, [#88](https://github.com/sagarworlds/Online-Exam-Platform/issues/88) | Waiting on you |
| The automated browser runs pass on Chromium, Firefox and WebKit | D19, #88 | Done in [#279](https://github.com/sagarworlds/Online-Exam-Platform/pull/279): three CI jobs, `Browser tests (chromium)`, `(firefox)` and `(webkit)`, run 11 tests each on every push (sign-in, sitting an exam, installing the app), and all passed on their first run. The API is stubbed in them, so they show the pages work in each engine, not that page and API agree. WebKit is not Safari, so the iOS Safari and Android Chrome row above still waits on you |

## 5. The pilot exam itself

| Gate | Note | Status |
|---|---|---|
| The paper is written and reviewed in the question bank, and the exam is published | | Waiting on you |
| The pilot is rehearsed on staging with a few real devices | | Waiting on staging |
| The roster is in | Staff import it as a CSV (FR-50, up to 1,000 rows at a time) from the batch roster page, and the report lists any rows that were left out. Check the member count against the school's list | Waiting on the roster |
| Staff who will run it have an account, with a second factor | The first administrator is created with `--create-first-administrator` ([ADR 0005](../adr/0005-first-administrator-job.md)) and adds the others in the app | Waiting on you |
| Someone answers candidates who report a problem during the exam | The report-an-issue queue (FR-42) is staffed | Waiting on you |

## The rule

- **Go** when every row in block 1 is done.
- For blocks 2 to 5, a row that is not done needs a written exception from the owner, with its reason and its date, in the same style as the exceptions in this folder. An exception is a choice, not a way to skip the row silently.
- **No go** if the owner cannot accept the risk of a row that is not done.
