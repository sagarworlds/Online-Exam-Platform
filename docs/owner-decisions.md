# Owner decisions and actions

**Date:** 2026-10-15
**Purpose:** the questions only the owner can answer, and the actions only the owner can take, so the remaining work can be finished. Everything else on the board is built, or is something engineering can build as soon as these are answered.
**Where the facts come from:** `exam-platform-requirements.md` (sections 14 to 17), `docs/compliance/`, `docs/observability.md`, `docs/additional-functionality.md`, `docs/deploy-render.md`, `tests/load/README.md`, the code, and the threads on the issues named below. Each decision says what it rests on. Prices and provider limits were not checked here.

## How to answer

Reply with the decision number and your choice, for example `D1 A, D11 A, D21 confirm`. Where a decision asks for a value, write it after the number: `D4: Jane Doe, privacy@example.org, <postal address>, 30 days`. Answer in any order and over several messages. I will record each answer in this file and on the issue it unblocks, and start the work it frees.

A decision marked **counsel** also needs a lawyer's confirmation. Your answer still helps: I build to your choice, and change it in one place if counsel disagrees.

## Start with these

- **D1, counsel.** It has the longest lead time on the board. It gates #72, #73, the minors' part of #255, the consent notices (#269), the NFR-12 exception, the WAF option and the pilot.
- **D11, hosting plan.** A load test, a restore drill and the weekly header check cannot be run on the free plans.
- **D3 and D4, retention and the grievance contact.** They block #79, #80 and the consent notices.

## All decisions at a glance

| ID | Decision | Unblocks | My recommendation | Counsel | Your answer |
|---|---|---|---|---|---|
| D1 | Send counsel the brief | #11, #72, #73, #255, #269, #91 | A: send all 16 questions now, asking for A, B, E14 and E15 first | is counsel | |
| D2 | Pilot cohort, and the share of candidates under 18 | #91, guardian consent as a launch blocker | A: adults only for the pilot | | |
| D3 | Retention periods | #79, #269 | A: you propose, counsel confirms; I draft the table | yes | |
| D4 | Grievance officer and answer time | #80, #269, privacy page | A: name the officer; keep 30 days unless counsel says otherwise | yes | |
| D5 | Marathi consent text | #269 | A: translate after counsel finalises English and Hindi | yes | |
| D6 | WhatsApp notices | switching WhatsApp on | A: keep it off until opt-in records exist | yes | |
| D7 | The unspent-codes page | support, counsel Q12 | A: keep it for the Super Admin only; I add which code was shown to the audit entry | yes | |
| D8 | Passwords for candidates | privacy page, data map | A: confirm candidates may set one, as the code allows today | | |
| D9 | Date of birth or age band | data map OP8 | A: keep the date of birth | yes | |
| D10 | Roster file fields | FR-50 | follows D2: A if adults only | | |
| D11 | Hosting plan | #84, #85, #86 header check, #91, #93 | A: paid staging now, production plans before the pilot | | |
| D12 | Expected load for the load test | #84 | A: use the script defaults for the first run | | |
| D13 | Availability and recovery targets for the pilot | #85 | B if the paid database plan can meet them, otherwise A | | |
| D14 | Log store and client IP addresses | #89 | A: an India-hosted store; I can compare options | yes | |
| D15 | Web application firewall | #86 | ask counsel first; B if accepted, otherwise A with an exception recorded | yes | |
| D16 | Question paper before the exam starts | #86 | A: accept access control and record the exception | | |
| D17 | The katex advisory | #86 | A: accept the low advisory until after the pilot | | |
| D18 | The exam time limit and WCAG 2.2.1 | #87 | A: record the limit as essential; extra time stays an accommodation | | |
| D19 | Accessibility sign-off, browsers and devices | #87, #88 | A: manual pass by you and volunteers, CI on three engines, a few real phones | | |
| D20 | NFR-4: an idempotency key on answer saves | M8 acceptance | B: add a sequence number | | |
| D21 | Policy choices made where the requirements were silent | confidence | confirm | | |

## Legal and privacy

### D1. Counsel's opinion on minors and proctoring ([#11](https://github.com/sagarworlds/Online-Exam-Platform/issues/11))

- **Unblocks:** #11 itself; FR-24 and FR-25 (#72, #73), because the requirements say legal opinion is needed before building FR-24, FR-25 and FR-27; the minors' part of #255; the review of the consent notices (#269); the NFR-12 exception; WAF option B; the retention table (D3); the pilot (#91).
- **Today:** the brief is written (`docs/compliance/counsel-brief.md`: 16 questions, A1 to F16). The issue is labelled `blocked` and has no comments.
- **Options:**
  - **A.** Send all 16 questions now. Ask counsel to answer A (children and monitoring), B (consent), E14 (processors and cross-border transfer) and E15 (India regions and the 180-day log rule) first, because those gate the build and the hosting.
  - **B.** Send only those four groups now and the rest later.
- **Recommendation:** A. It costs nothing extra, and counsel can answer in the order asked.
- **You decide:** who counsel is, and the date by which you need the first answers. I record the opinion on #11 as the brief asks.

### D2. Pilot cohort, and the share of candidates under 18 (requirements section 17, question 2)

- **Unblocks:** the pilot plan (#91), and whether guardian consent is a launch blocker. The requirements say it is if most candidates are under 18.
- **Today:** each candidate's date of birth is stored. Since [#267](https://github.com/sagarworlds/Online-Exam-Platform/pull/267) a candidate under 18 cannot start an attempt until a guardian has confirmed the link. Consent to a notice version is not checked yet (data map gap G3), and counsel's answers on minors are open (D1). Camera, screen and face features are not built, and the risk scan and time per question are off for under-18 candidates by default.
- **Options:**
  - **A.** The pilot uses adult candidates only. Minors come in after counsel's opinion and the remaining guardian-consent work.
  - **B.** The pilot includes minors. Then the consent check (G3), counsel's answers A and B, and the roster fields (D10) have to be done first.
- **Recommendation:** A. The pilot can then go ahead without waiting for counsel on minors.
- **Also tell me:** roughly what share of launch candidates are under 18.

### D3. Retention periods ([#79](https://github.com/sagarworlds/Online-Exam-Platform/issues/79), data map OP1)

- **Unblocks:** #79 (FR-47), the privacy notice text (#269, OP-D), the public privacy page.
- **Today:** only one-time codes, password-reset links and sessions are deleted, one day after they expire. That grace period is a provisional default (`Identity:Retention`). No other data has a period: every row of the data map says "to be decided by the owner". The audit log is never purged, and a retention job must not purge audit rows younger than 180 days (`docs/observability.md`).
- **Options:**
  - **A.** You propose a period for each row of the data map, and counsel confirms.
  - **B.** Wait for counsel to propose them.
- **Recommendation:** A. I draft a table with a proposed period per row for you to edit, so counsel reviews something concrete. Counsel's questions C8 to C10 cover sightings, focus violations, warnings, accommodation notes, issue reports, the consent record, the audit log and proctoring media.

### D4. Grievance officer and the data-request answer time ([#80](https://github.com/sagarworlds/Online-Exam-Platform/issues/80), data map OP2)

- **Unblocks:** #80 (FR-48), the consent notices (#269, OP-C), the privacy page.
- **Today:** no grievance officer is named anywhere in the repo. The privacy page's contact section tells people to write to the address that invited them, with "Privacy" in the subject. A comment at the top of that page says to set the operator and grievance contact before the page's address goes to Meta. Data requests are due in 30 days by default (`Identity:DataRequests:ServiceDays`), which is an engineering default and not a decision.
- **Options:**
  - **A.** Name an officer and a dedicated mailbox. Keep 30 days unless counsel says otherwise.
  - **B.** Keep the invitation address as the contact for now. The notices ask for the officer's name and address (OP-C), so they would stay incomplete.
- **Recommendation:** A.
- **Reply with:** the name, e-mail address, postal address, and the answer time in days.

### D5. Marathi consent text ([#269](https://github.com/sagarworlds/Online-Exam-Platform/issues/269), OP-I)

- **Unblocks:** #269.
- **Today:** the interface has Marathi, but no Marathi consent text is drafted (data map gap G15). English and Hindi drafts are in `docs/compliance/consent-notices.json` and wait for counsel.
- **Options:**
  - **A.** Translate the Marathi after counsel finalises the English and Hindi, so it is translated once. A recorded consent's text must never change (OP-K).
  - **B.** Ship the consent notices in English and Hindi only, and record Marathi as a gap.
- **Recommendation:** A, with a Marathi speaker reviewing it.

### D6. WhatsApp notices

- **Unblocks:** switching WhatsApp on.
- **Today:** the privacy page says WhatsApp notices go only to people who agreed, and WhatsApp's own policy requires the same, but the platform records no such agreement: there is no consent purpose for it. Everything to do with WhatsApp ships switched off (`docs/additional-functionality.md`, N2 and N3).
- **Options:**
  - **A.** Keep it off until opt-in and opt-out records exist (the `WhatsAppNotices` purpose in #269 and the next piece of FR-39).
  - **B.** Switch it on with your recorded statement that the recipients agreed.
- **Recommendation:** A.

### D7. The unspent-codes page (data map OP9, counsel question 12)

- **Unblocks:** support use of the page, and counsel's answer on it.
- **Today:** the page shows candidates' unspent sign-in and registration codes to anyone holding `identity.otp.read`. Only the Super Admin role holds it. Each read is audited, but the audit entry does not say which code was shown.
- **Options:**
  - **A.** Keep it for the Super Admin only. I add which code was shown to the audit entry, a small change.
  - **B.** Switch it off in production. Support asks the person to request a new code.
  - **C.** Keep it as it is.
- **Recommendation:** A, and B if counsel's answer to question 12 is no.

### D8. Passwords for candidates (data map OP10)

- **Today:** the privacy page says "a password if you use one". The web app has a Register page and a Password tab on sign-in, and the password-reset handler sets a hash on any account.
- **Options:** **A.** Candidates may set a password, as the code allows today. **B.** Passwords are for staff only; I change the code and the privacy page to match.
- **Recommendation:** A, if candidates register themselves. Tell me if that is not intended.

### D9. Date of birth or age band (data map OP8)

- **Today:** the full date of birth is stored.
- **Options:** **A.** Keep it. Exact age is what the 18th-birthday rules need, such as when a guardian link ends. **B.** Store an age band only. That minimises the data, but the platform could not tell when someone turns 18.
- **Recommendation:** A, with counsel confirming that it meets data minimisation.

### D10. Roster file fields (data map OP12)

- **Today:** FR-50 asks the roster file to carry a name and, for minors, a guardian contact. The importer reads an e-mail and an optional phone only.
- **Options:** **A.** Keep the importer and change the FR-50 text to match. **B.** Extend the importer to read a name and a guardian contact.
- **Recommendation:** follows D2. A if the pilot is adults only; B once minors are in.

## Hosting and operations

### D11. Hosting plan ([#84](https://github.com/sagarworlds/Online-Exam-Platform/issues/84), [#85](https://github.com/sagarworlds/Online-Exam-Platform/issues/85), [#91](https://github.com/sagarworlds/Online-Exam-Platform/issues/91), [#93](https://github.com/sagarworlds/Online-Exam-Platform/issues/93))

- **Unblocks:** the load test (#84), the restore drill (#85), the weekly header check (#86), the pilot (#91), go-live (#93).
- **Today:** the API runs on Render's free web service and the database on Neon's free plan (`docs/deploy-render.md`). The API sleeps after about 15 minutes idle, and the first request afterwards takes up to a minute. Neon sleeps after 5 minutes and holds 0.5 GB. The free Brevo plan allows about 300 e-mails a day. There is no staging environment (comment on #84). A load test on free plans would measure cold starts, not capacity.
- **Options:**
  - **A.** Paid Render and Neon for a staging environment now. Decide the production plans before the pilot.
  - **B.** Paid plans for staging and production now.
  - **C.** Move to a provider with an India region. That reopens the NFR-12 decision ([#22](https://github.com/sagarworlds/Online-Exam-Platform/issues/22) was closed as not planned), so it needs D1 first.
- **Recommendation:** A. It unblocks the load test, the restore drill and the header check at the lowest cost, and delays the production commitment until the numbers exist.
- **Not checked:** I have not priced the plans or verified their backup and restore limits. Confirm on the providers' pages, or ask me to compare them.
- **Also needed:** a paid e-mail plan if the pilot sends more than about 300 e-mails a day. Sign-in codes, invitations and results all use it.

### D12. Expected load for the load test (`tests/load/README.md`, requirements section 17, question 1)

- **Unblocks:** the first real run of #84.
- **Today:** the scripts assume 10,000 concurrent candidates at peak (the section 9 tier-1 ceiling), tested at 2x, which is 20,000. They assume each candidate saves an answer about every 20 seconds and that the whole cohort opens the paper within 2 minutes. That makes 1,000 answer saves and 167 paper reads per second at 2x. The requirements state no expected peak.
- **Options:** **A.** Use these defaults for the first run. **B.** Give me your own numbers: candidates at launch and in year 2, and the exam start pattern.
- **Recommendation:** A. A smoke run can change the numbers, and real figures can follow once the first schools are known. The year-2 figure decides when the exam runtime becomes its own service.

### D13. Availability and recovery targets for the pilot ([#85](https://github.com/sagarworlds/Online-Exam-Platform/issues/85))

- **Unblocks:** #85, and the go-live checklist.
- **Today:** NFR-3 asks for 99.9% availability overall and 99.95% in scheduled exam windows, with a change freeze before big exams. NFR-10 asks for losing at most 5 minutes of data (recovery point) and being back within 30 minutes (recovery time), with automated backups and tested restores. The repo has no backup schedule, no restore runbook and no change-freeze process. The only backup it describes is a manual dump when copying data into Neon.
- **Options:**
  - **A.** For the pilot, accept a lighter target (daily backups and one tested restore) and record the gap. Meet NFR-10 before launch.
  - **B.** Meet NFR-3 and NFR-10 as written for the pilot too. That needs a database plan with point-in-time restore to within 5 minutes, which is not verified.
  - **C.** Change the targets in the requirements.
- **Recommendation:** B if the paid database plan can meet it, otherwise A with the gap recorded. Real candidates' answers are at stake in the pilot.
- **I write either way:** `docs/runbooks/restore.md` and an ADR. The runbook says where the key-ring key is kept and how it is restored apart from the database (`docs/adr/0003-key-ring-protection.md`).

### D14. Log store and client IP addresses ([#89](https://github.com/sagarworlds/Online-Exam-Platform/issues/89), `docs/observability.md`)

- **Unblocks:** #89 and NFR-13.
- **Today:** the application writes JSON logs to its output and keeps none. Startup refuses a declared retention below 180 days, but the setting only records what the store does: nothing keeps logs for 180 days yet. The NFR-12 exception records that Render's logs are outside India. Request logs leave out client IP addresses, because an address is personal data.
- **Options for the store:**
  - **A.** Choose an India-hosted log store with retention of at least 180 days, and set `LogRetention:Days` to match.
  - **B.** Keep the logs outside India under the NFR-12 exception, if counsel accepts it.
- **Options for IP addresses:** **A.** Keep leaving them out. **B.** Record them in security logs only, with counsel's agreement.
- **Recommendation:** store A. I can research and compare three or four India-hosted options for you. IP addresses are counsel's call with yours.

### D15. Web application firewall ([#86](https://github.com/sagarworlds/Online-Exam-Platform/issues/86), `docs/compliance/waf-decision.md`)

- **Unblocks:** the NFR-5 WAF item.
- **Today:** the API has rate limits, security headers and authorization checks, but nothing that inspects request bodies for attack patterns. A WAF in front would see every request, including personal data, so it is another processor under NFR-12.
- **Options:** **A.** No WAF, with an accepted exception recorded. **B.** Cloudflare in front of Render. **C.** Google Cloud Armor. **D.** AWS WAF. **E.** Azure Front Door. C, D and E mean moving hosting. Prices and set-up work are in the decision file, and several figures there are marked unverified.
- **Recommendation:** ask counsel first. If counsel accepts an edge provider holding personal data, B (Free or Pro) is the smallest change. If not, A with the exception and its reason recorded.

### D16. The question paper before the exam starts ([#86](https://github.com/sagarworlds/Online-Exam-Platform/issues/86), `docs/compliance/nfr-5-security-checks.md`)

- **Today:** NFR-5 asks for the paper to be encrypted until the exam starts. The code does not do that. It keeps the paper back by access control: no candidate has an attempt before the window opens. Staff can open the preview route at any time, by design.
- **Options:**
  - **A.** Accept access control as the control, and record the exception with its reason.
  - **B.** Build release at the start time, such as a key issued only when the window opens. That is a large change.
- **Recommendation:** A for the pilot. Revisit it if counsel or a security reviewer asks for B.

### D17. The katex advisory ([#86](https://github.com/sagarworlds/Online-Exam-Platform/issues/86))

- **Today:** `npm audit` reports one low advisory in `katex` (prototype pollution). It is fixed only in katex 0.19.0, which is a breaking change. It does not fail the high-severity gate.
- **Options:** **A.** Accept it and record that. **B.** Upgrade, with a check that maths still renders correctly across real questions.
- **Recommendation:** A until after the pilot. Maths is exam content, so a rendering change is a bigger risk than a low advisory.

## Accessibility and reliability

### D18. The exam time limit and WCAG 2.2.1 ([#87](https://github.com/sagarworlds/Online-Exam-Platform/issues/87), audit item A-06)

- **Today:** the exam warns at five minutes and at one minute, and announces time up. WCAG 2.2.1 also needs either a recorded exception for a time limit that is essential, or a way to extend the time. Neither is in place (`docs/compliance/accessibility-audit.md`). Staff can already give a candidate extra time as an accommodation (FR-49).
- **Options:**
  - **A.** Record that the time limit is essential to the assessment, with per-candidate extra time as the adjustment.
  - **B.** Add a way for a candidate to extend their own time. That changes what the exam measures.
- **Recommendation:** A. Confirm it with whoever signs off accessibility (D19).

### D19. Accessibility sign-off, browsers and devices ([#87](https://github.com/sagarworlds/Online-Exam-Platform/issues/87), [#88](https://github.com/sagarworlds/Online-Exam-Platform/issues/88))

- **Today:** the automated audit is clean and every audit finding is fixed except A-06 (D18). What remains is a manual pass with a screen reader (NVDA or VoiceOver) and a keyboard-only session, and real-device checks (iOS Safari, Android Chrome). Only Chromium has been run; Firefox, Safari and Edge are untested. The browser list for NFR-8 is "the current two versions of Chrome, Edge, Firefox and Safari".
- **Options:**
  - **A.** You and a few volunteers do the manual pass from a checklist I write. I set up automated runs in Chromium, Firefox and WebKit (WebKit is not Safari). You check a few real phones.
  - **B.** Hire an external accessibility auditor and use a device lab.
- **Recommendation:** A for the pilot. Choose B if a school or regulator asks for an external report.

### D20. NFR-4: an idempotency key on answer saves

- **Unblocks:** the M8 acceptance, which says the load test passes NFR-1 and NFR-4.
- **Today:** NFR-4 asks for a client buffer, retry with idempotency keys, and durable server writes. The web client keeps the latest pending answer for each question, keeps it on the device so a reload does not lose it, and retries after 2, 4, 8 and 16 seconds, then every 30. The server stores an answer with its own clock and replaces any earlier answer to the same question. Repeating the same save is harmless. There is no client sequence number, so a delayed older save that arrives after a newer one would replace it. Whether that can happen depends on the client having only one save per question in flight, which I have not proven.
- **Options:**
  - **A.** Accept "replace by arrival" plus the client's queue as meeting NFR-4, and record that.
  - **B.** Add a sequence number to answer saves, so the server ignores a save older than the one it holds.
- **Recommendation:** B. It is a small API change with tests, and it removes the only reordering risk.

## Policy

### D21. Policy choices made where the requirements were silent (`docs/additional-functionality.md`)

Confirm these, or tell me what to change:

- Staff can give a candidate another attempt, and a candidate who has used all theirs can ask for one with a reason. My exams marks the highest-scoring submitted attempt as Best, and nothing is stored as the exam's official score (N6).
- A later attempt shows questions and options shuffled even when the first did not, and an option can be pinned in place (B2).
- Once a candidate has answered a question, only its wording can be changed, and a question cannot be deleted while an exam holds it (B4).
- Disputes (B8): a candidate can dispute for seven days after results are released, once per question per attempt. Staff's answer is final, and a dispute is accepted only by correcting the answer key, which settles every open dispute about that question.

## Actions only you can take

These need no decision, only you or an account of yours.

| # | Action | Why | Where |
|---|---|---|---|
| A1 | Check that `DataProtection__KeyEncryptionKey` exists on the Render API service, and copy its value to a password manager, apart from the database backups. | The key-ring change (#86, commit `11fb9bd`) cannot deploy without it, and losing it loses the question content. | `docs/deploy-render.md`, step 5 |
| A2 | Set the GitHub repository variables `PUBLIC_WEB_URL` and `PUBLIC_API_URL`. | The weekly security-header check stays red until both are set. | #86 |
| A3 | Set the static site's security headers, in the Render Dashboard or in `render.yaml` once Render's syntax is checked. | The site sends none yet. The weekly check reports what is actually served. | `docs/compliance/nfr-5-security-checks.md` |
| A4 | Book a penetration test. | NFR-5 asks for a security test with no findings, and the automated checks do not replace one. | #86 |
| A5 | In the Render and Neon consoles, confirm the regions and write them into the NFR-12 exception. Turn on India local storage for the WhatsApp business number. Confirm the Brevo data-processing agreement and Meta's terms. | These are conditions the exception lists before launch. | `docs/compliance/nfr-12-exception.md` |
| A6 | Put the operator and grievance contact into the privacy page's contact section, after D4. | A comment on that page says to do it before giving the address to Meta. | `apps/web/public/privacy.html` |
| A7 | Close milestones M1 and M3 in the GitHub UI. | They have no open issues, and my tools cannot close milestones. | GitHub |
| A8 | Close [#235](https://github.com/sagarworlds/Online-Exam-Platform/issues/235), or tell me to. | You approved the Hindi and Marathi wording on 12 October and nothing was changed, but the issue is still open. | #235 |
| A9 | Raise the rate limits on staging for the load-test window: `RateLimiting:Global` and `Identity:RateLimits:PasswordLogin`. | The limits would otherwise stop the run. | `tests/load/README.md` |
| A10 | Run the manual accessibility pass and the real-device checks, once I have written the checklist (D19). | They need people and devices. | #87, #88 |
| A11 | Record an owner decision, with its date, before switching on `ExamRuntime:TimePerQuestion:MinorsEnabled` or `Proctoring:MinorsScanEnabled`. | Both are off for candidates under 18, and the exception says enabling needs a recorded decision. | `docs/compliance/minors-time-per-question-exception.md` |

## What I will decide myself

These are engineering choices. They need no answer, but you can object.

- **OP-G, the reference for each consent notice:** a URL under the web app's public folder, with the version and the language in the path. A recorded text never changes (OP-K), so each version gets its own address.
- **OP-H, how Hindi is stored:** one notice version row per language, with no schema change. A consent record then points at the exact text the person saw.
- **OP-J, the new purpose keys:** the `WhatsAppNotices` and `MinorGuardianConsent` values, and their seeded versions, once counsel has approved the texts.
- **Drafts for you to edit:** the retention table (D3), the restore runbook and ADR (D13), the accessibility checklist (D19), and a pilot go/no-go checklist that cites #84, #86 and #87.

## Already decided, for the record

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

When a decision is made, its row moves to "Already decided" with the date and where it is recorded, and the issue it unblocks gets a comment. New questions are added here, so there is one list to answer from.
