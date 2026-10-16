# Owner decisions and actions

**Date:** 2026-10-15, answers recorded the same day
**Purpose:** the questions only the owner can answer, and the actions only the owner can take, so the remaining work can be finished. Everything else on the board is built, or is something engineering can build as soon as these are answered.
**Where the facts come from:** `exam-platform-requirements.md` (sections 14 to 17), `docs/compliance/`, `docs/observability.md`, `docs/additional-functionality.md`, `docs/deploy-render.md`, `tests/load/README.md`, the code, and the threads on the issues named below. The providers' own pages cannot be opened from the environment engineering works in (the lookups fail), so prices and limits were not checked there. The few figures quoted below come from search summaries and are marked.

## Where this stands

The first version of this pack (git commit `553b9f8`) listed 21 decisions, each with its options and a recommendation. On 2026-10-15 the owner replied "accept all", which takes every recommendation except where it rests on something engineering could not check: D4, D13 and D15.

So **18 decisions are settled** and recorded below, each with the file that now holds it. **Three are open.** Everything else waiting on the owner is an action (the table after the decisions).

### Corrections to the first version

- **D10 said the roster importer reads an e-mail and an optional phone, but there was no importer.** Staff could only add batch members one at a time (`POST /v1/batches/{id}/members`), `RosterValidator` had no caller outside its own tests, and issue [#42](https://github.com/sagarworlds/Online-Exam-Platform/issues/42) was closed without the import. The decision stands (the roster carries an e-mail and an optional phone only). The import is now built, in [#273](https://github.com/sagarworlds/Online-Exam-Platform/issues/273), and `RosterValidator` is gone: the batch itself judges each row.
- **Action A7 said to close milestones M1 and M3.** GitHub shows M1, M2 and M3 closed on 2026-10-14, so A7 is removed.

## Still open

Reply with the decision number and your choice, for example `D13 B`. Where a decision asks for a value, write it after the number: `D4: Jane Doe, privacy@example.org, <postal address>, 30 days`. Answer in any order and over several messages. I will record each answer in this file and on the issue it unblocks.

### D4. Grievance officer and the data-request answer time ([#80](https://github.com/sagarworlds/Online-Exam-Platform/issues/80), data map OP2)

- **Unblocks:** #80 (FR-48), the consent notices (#269, OP-C), the privacy page.
- **Today:** no grievance officer is named anywhere in the repo. The privacy page's contact section tells people to write to the address that invited them, with "Privacy" in the subject. A comment at the top of that page says to set the operator and grievance contact before the page's address goes to Meta. Data requests are due in 30 days by default (`Identity:DataRequests:ServiceDays`), which is an engineering default and not a decision.
- **Options:**
  - **A.** Name an officer and a dedicated mailbox. Keep 30 days unless counsel says otherwise.
  - **B.** Keep the invitation address as the contact for now. The notices ask for the officer's name and address (OP-C), so they would stay incomplete.
- **Recommendation:** A. Counsel (D1) may change the answer time.
- **Reply with:** the name, e-mail address, postal address, and the answer time in days.

### D13. Availability and recovery targets for the pilot ([#85](https://github.com/sagarworlds/Online-Exam-Platform/issues/85))

- **Unblocks:** #85, and the go-live checklist.
- **Today:** NFR-3 asks for 99.9% availability overall and 99.95% in scheduled exam windows, with a change freeze before big exams. NFR-10 asks for losing at most 5 minutes of data (recovery point) and being back within 30 minutes (recovery time), with automated backups and tested restores. The repo has no backup schedule, no restore runbook and no change-freeze process. The only backup it describes is a manual dump when copying data into Neon.
- **Reported, not checked:** search summaries of Neon's pricing say the Free plan keeps 6 hours of history to restore from, the Launch plan up to 7 days and the Scale plan up to 30 days, with paid plans from a $5 monthly minimum. Neon's own page could not be opened. If the figures hold, a paid plan can restore to a point in time within the last days, which is what the 5-minute recovery point needs on the database side. The 30-minute recovery time, the API's single instance and the key (`docs/adr/0003-key-ring-protection.md`) are separate questions.
- **Options:**
  - **A.** For the pilot, accept a lighter target (daily backups and one tested restore) and record the gap. Meet NFR-10 before launch.
  - **B.** Meet NFR-3 and NFR-10 as written for the pilot too. That needs a database plan with point-in-time restore to within 5 minutes.
  - **C.** Change the targets in the requirements.
- **Recommendation:** B, if you confirm on Neon's pricing page that the plan you buy for staging and production keeps point-in-time history for at least several days. Otherwise A, with the gap recorded. Real candidates' answers are at stake in the pilot.
- **I write after your answer:** `docs/runbooks/restore.md` and an ADR. The runbook says where the key-ring key is kept and how it is restored apart from the database.

### D15. Web application firewall ([#86](https://github.com/sagarworlds/Online-Exam-Platform/issues/86), `docs/compliance/waf-decision.md`)

- **Unblocks:** the NFR-5 WAF item.
- **Today:** the API has rate limits, security headers and authorization checks, but nothing that inspects request bodies for attack patterns. A WAF in front would see every request, including personal data, so it is another processor under NFR-12.
- **Options:** **A.** No WAF, with an accepted exception recorded. **B.** Cloudflare in front of Render. **C.** Google Cloud Armor. **D.** AWS WAF. **E.** Azure Front Door. C, D and E mean moving hosting. Prices and set-up work are in the decision file, and several figures there are marked unverified.
- **Recommendation:** ask counsel first. Question 17 in the counsel brief asks it (added on 2026-10-15). If counsel accepts an edge provider holding personal data, B (Free or Pro) is the smallest change. If not, A with the exception and its reason recorded. You can also reply `D15 A` now to go ahead without a WAF and record the exception, and change it later.

## Decided on 2026-10-15

| ID | Decision | Answer | Recorded in | What happens next |
|---|---|---|---|---|
| D1 | Counsel's opinion on minors and proctoring ([#11](https://github.com/sagarworlds/Online-Exam-Platform/issues/11)) | **A.** Send counsel the whole brief now, asking for A, B, E14 and E15 first. The brief has 18 questions: the 16 of the first version, and two added on 2026-10-15 (17 for D15, 18 for D9) | `docs/compliance/counsel-brief.md`, `docs/compliance/counsel-cover-note.md` | **You:** choose counsel, set the date you need the first answers by, and send the brief (A12). I record the opinion on #11 |
| D2 | Pilot cohort, and the share under 18 | **A.** The pilot uses adult candidates only | `docs/compliance/pilot-go-no-go.md` | The pilot does not wait for counsel on minors. It still waits for counsel on the NFR-12 exception and the consent notices, because adults' data is personal data too. Minors come in after counsel's answers and the remaining guardian-consent work |
| D3 | Retention periods ([#79](https://github.com/sagarworlds/Online-Exam-Platform/issues/79)) | **A.** You propose a period for each row of the data map, and counsel confirms | `docs/compliance/retention-proposal.md` | **You:** edit the proposed periods (A13). Then I build the retention job (#79) and finish the privacy notice text (#269) |
| D5 | Marathi consent text ([#269](https://github.com/sagarworlds/Online-Exam-Platform/issues/269)) | **A.** Translate after counsel finalises the English and Hindi, so it is translated once; a Marathi speaker reviews it | #269 | Waits for counsel (D1) |
| D6 | WhatsApp notices | **A.** Keep WhatsApp off until opt-in and opt-out records exist | `docs/additional-functionality.md` | The `WhatsAppNotices` purpose arrives with #269, and the records with the next piece of FR-39 |
| D7 | The unspent-codes page | **A.** Keep it for the Super Admin only, and record which code was shown in each read's audit entry (the challenge and its purpose, never the code) | `docs/additional-functionality.md`, data map OP9 | Built and merged in [#274](https://github.com/sagarworlds/Online-Exam-Platform/pull/274): each read's audit entry now lists the challenge and purpose of every code shown. If counsel's answer to question 12 is no, switch the page off in production |
| D8 | Passwords for candidates | **A.** Candidates may set a password, as the code allows | data map OP10 | None |
| D9 | Date of birth or age band | **A.** Keep the full date of birth, because the 18th-birthday rules need the exact age | data map OP8, counsel brief question 18 | Counsel confirms it meets data minimisation |
| D10 | Roster file fields | **A.** The roster carries an e-mail and an optional phone only. A candidate's name comes from their account, and a minor's guardian is linked through the guardian flow | `exam-platform-requirements.md` (FR-50), data map OP12 | The CSV import is built ([#273](https://github.com/sagarworlds/Online-Exam-Platform/issues/273), merged in [#275](https://github.com/sagarworlds/Online-Exam-Platform/pull/275)). Revisit when candidates under 18 are admitted |
| D11 | Hosting plan | **A.** Paid Render and Neon for a staging environment now. Decide the production plans before the pilot | `docs/runbooks/staging.md`, `docs/additional-functionality.md` (S1) | **You:** create the staging services (A14). Then the load test (#84) and the restore drill (#85) can run |
| D12 | Expected load for the load test | **A.** Use the script defaults for the first run: 10,000 concurrent candidates at peak, tested at 2x, a save every 20 seconds, the whole cohort opening its paper within 2 minutes | `tests/load/README.md` | Runs when staging exists. Real figures replace the defaults once the first schools are known |
| D14 | Log store and client IP addresses ([#89](https://github.com/sagarworlds/Online-Exam-Platform/issues/89)) | **A.** An India-hosted log store with retention of at least 180 days. Client IP addresses stay out of the request logs | `docs/observability.md`, `docs/compliance/log-store-options.md` | **You:** choose the store after counsel answers question 15 (A15) |
| D16 | The question paper before the exam starts | **A.** Accept access control as the control for the pilot, and record the exception | `docs/compliance/nfr-5-paper-before-start-exception.md` | Ask the penetration test (A4) to look at it |
| D17 | The katex advisory | **A.** Accept the low advisory until after the pilot | `docs/compliance/nfr-5-security-checks.md` | Revisit after the pilot |
| D18 | The exam time limit and WCAG 2.2.1 | **A.** Record the limit as essential. Extra time stays an accommodation | `docs/compliance/accessibility-audit.md` (A-06 closed) | Confirm it with whoever signs off accessibility (D19) |
| D19 | Accessibility sign-off, browsers and devices ([#87](https://github.com/sagarworlds/Online-Exam-Platform/issues/87), [#88](https://github.com/sagarworlds/Online-Exam-Platform/issues/88)) | **A.** A manual pass by you and volunteers, automated runs on Chromium, Firefox and WebKit, and a few real phones | `docs/compliance/accessibility-manual-checklist.md` | **You:** run the pass and the phone checks (A10). Engineering sets up the three-engine runs (below) |
| D20 | NFR-4: an idempotency key on answer saves | **B.** Add a sequence number to answer saves, so the server ignores a save older than the one it holds | NFR-4, `docs/adr/0004-answer-save-sequence-numbers.md` | Built and merged in [#274](https://github.com/sagarworlds/Online-Exam-Platform/pull/274). Old clients keep working. The load test (#84) now sends the numbers, so it measures the real save |
| D21 | Policy choices made where the requirements were silent | **Confirmed.** N6, B2, B4 and B8 stand as built | `docs/additional-functionality.md` | None |

## What engineering does next

| Item | Depends on |
|---|---|
| The unspent-codes audit entry (D7) and the answer-save sequence number (D20): merged in [#274](https://github.com/sagarworlds/Online-Exam-Platform/pull/274) | Nothing left. A1 is done |
| The CSV roster import (FR-50, [#273](https://github.com/sagarworlds/Online-Exam-Platform/issues/273)), with a report of the rows left out: merged in [#275](https://github.com/sagarworlds/Online-Exam-Platform/pull/275) | Nothing left |
| Test accounts for the load test (`--create-load-test-candidates` and `--enrol-load-test-candidates`, [ADR 0006](adr/0006-load-test-candidate-pool.md)): merged in [#277](https://github.com/sagarworlds/Online-Exam-Platform/pull/277). They create and enrol thousands of candidates on addresses that cannot exist, and refuse to run unless switched on | Staging (A14), for the full run |
| A safe way to create the first administrator in a deployed environment (`--create-first-administrator`, [ADR 0005](adr/0005-first-administrator-job.md)): merged in [#276](https://github.com/sagarworlds/Online-Exam-Platform/pull/276), with no password to store | The e-mail it sends needs the mail settings (A14 for staging) |
| Automated browser runs on Chromium, Firefox and WebKit (D19, #88) | Nothing |
| The retention job (#79) and the privacy notice text (#269) | The edited retention periods (A13) and counsel (D1) |
| The restore runbook and ADR (#85) | D13 |
| The consent notices in the ledger (#269), including Marathi | Counsel (D1), D4 |
| The load test (#84) and the restore drill (#85) | Staging (A14) |

## Actions only you can take

These need no decision, only you or an account of yours.

| # | Action | Why | Where |
|---|---|---|---|
| A2 | Set the GitHub repository variables `PUBLIC_WEB_URL` and `PUBLIC_API_URL`. | The weekly security-header check stays red until both are set. | #86 |
| A3 | Set the static site's security headers, in the Render Dashboard or in `render.yaml` once Render's syntax is checked. | The site sends none yet. The weekly check reports what is actually served. | `docs/compliance/nfr-5-security-checks.md` |
| A4 | Book a penetration test, and ask it to look at the paper-before-start exception (D16). | NFR-5 asks for a security test with no findings, and the automated checks do not replace one. | #86 |
| A5 | In the Render and Neon consoles, confirm the regions and write them into the NFR-12 exception. Turn on India local storage for the WhatsApp business number. Confirm the Brevo data-processing agreement and Meta's terms. | These are conditions the exception lists before launch. | `docs/compliance/nfr-12-exception.md` |
| A6 | Put the operator and grievance contact into the privacy page's contact section, after D4. | A comment on that page says to do it before giving the address to Meta. | `apps/web/public/privacy.html` |
| A8 | Close [#235](https://github.com/sagarworlds/Online-Exam-Platform/issues/235), or tell me to. | You approved the Hindi and Marathi wording on 12 October and nothing was changed, but the issue is still open. | #235 |
| A9 | Raise the rate limits on staging for the load-test window: `RateLimiting:Global` and `Identity:RateLimits:PasswordLogin`. | The limits would otherwise stop the run. | `tests/load/README.md` |
| A10 | Run the manual accessibility pass and the real-device checks from the checklist (D19). | They need people and devices. | `docs/compliance/accessibility-manual-checklist.md`, #87, #88 |
| A11 | Record an owner decision, with its date, before switching on `ExamRuntime:TimePerQuestion:MinorsEnabled` or `Proctoring:MinorsScanEnabled`. | Both are off for candidates under 18, and the exception says enabling needs a recorded decision. | `docs/compliance/minors-time-per-question-exception.md` |
| A12 | Choose counsel, set the date you need the first answers by, and send the brief with the cover note (D1). | It has the longest lead time on the board. | `docs/compliance/counsel-cover-note.md` |
| A13 | Edit the proposed retention periods (D3). | #79 and the privacy notice cannot be finished without them. | `docs/compliance/retention-proposal.md` |
| A14 | Create the paid staging services on Render and Neon, and tell me the staging address (D11). | The free API sleeps when idle, so a load test on it would measure cold starts, not capacity. A restore drill should run on the kind of plan production will use. | `docs/runbooks/staging.md` |
| A15 | Choose a log store after counsel answers question 15 (D14). | #89 cannot be finished without one. | `docs/compliance/log-store-options.md` |

A7 (close milestones M1 and M3) is done: GitHub shows M1, M2 and M3 closed on 2026-10-14.

A1 (the key-ring key on Render) is done, as reported by the owner on 2026-10-16. The first deploy of the key-ring change failed because the API service had no `DataProtection__KeyEncryptionKey` (the log said `DataProtection:KeyEncryptionKey is not set`; the previous version kept serving), so the owner added it and kept a copy in a password manager. Engineering cannot see Render, so this rests on the owner's word.

## What I decide myself

These are engineering choices. They need no answer, but you can object.

- **OP-G, the reference for each consent notice:** a URL under the web app's public folder, with the version and the language in the path. A recorded text never changes (OP-K), so each version gets its own address.
- **OP-H, how Hindi is stored:** one notice version row per language, with no schema change. A consent record then points at the exact text the person saw.
- **OP-J, the new purpose keys:** the `WhatsAppNotices` and `MinorGuardianConsent` values, and their seeded versions, once counsel has approved the texts.

## Decided earlier, for the record

| Decision | Recorded in |
|---|---|
| Keep Render, Neon, Brevo and Meta, and record the data-location exception. Not reviewed by counsel. | `docs/compliance/nfr-12-exception.md` |
| Fullscreen is offered, not required. Warn at three departures, end the attempt at five. | #255, #268 |
| Time per question is not recorded for candidates under 18 unless the switch is turned on. Accepted 2026-10-12. | `docs/compliance/minors-time-per-question-exception.md` |
| Marathi is a third interface and question language. | `docs/additional-functionality.md`, B1 |
| The Hindi and Marathi wording of the dispute-accepted notice is approved. | #235, comment of 2026-10-12 |

## Open but not blocking

- Which regional languages come after English, Hindi and Marathi, and in what order (requirements section 17, question 3).
- The year-2 peak, which decides when the exam runtime is split into its own service (section 17, question 1).

## Keeping this current

When a decision is made, it goes into "Decided" with the date and where it is recorded, and the issue it unblocks gets a comment. New questions are added under "Still open", so there is one list to answer from.
