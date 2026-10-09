# DPDP data map

**Milestone:** M0 Discovery & legal, issue [#12](https://github.com/sagarworlds/Online-Exam-Platform/issues/12).
**Status:** draft for counsel review. This is an engineering inventory, not legal advice. Purposes, retention periods and the India-region decision need sign-off before this map is treated as final.
**Source:** the code on `main` at `4bedf1b`, read module by module. Requirements: section 7 (India compliance) and section 10 (data model).

## How to read it

Each row names the personal data a module holds, whose it is, why it is held, how long it is kept **today**, and who processes it. "Kept today" describes what the code does, not what the policy should say. Nothing in the code removes personal data automatically (see gap 1), so every row is kept until someone deletes it by hand.

The browser keeps one setting, the interface language, in `localStorage`. It holds no name or identifier.

## 1. Identity and sign-in (`Identity`)

| Data | Whose | Purpose | Kept today | Processor |
|---|---|---|---|---|
| User: e-mail or phone (at least one), display name, date of birth, roles, status, password hash (staff only) | Everyone with an account | Account, sign-in, and the age band that gates guardian consent | Until the account is removed; no removal job exists | Hosting, database |
| OTP challenge: destination (e-mail or phone), code hash, revealable code, attempts, expiry | Anyone signing in or registering | Sign-in and registration codes | Rows stay after use; the revealable code is cleared once the code is used or replaced | Brevo (e-mail); Meta WhatsApp Cloud API (phone) |
| User session: session token hash, IP address, device fingerprint, issued and expiry times | Signed-in users | Keeping a session, and noticing a second device | Rows stay after expiry | Hosting, database |
| Password reset token: user id, token hash, times | Staff | Password reset | Rows stay after use | Hosting, database |

## 2. Audit trail (`Admin`, and the audit log written by every module)

| Data | Whose | Purpose | Kept today | Processor |
|---|---|---|---|---|
| Audit entry: time, actor user id and role, action, entity, metadata, correlation id | Staff and candidates who act in the system | Accountability; CERT-In log expectations | Kept; no purge | Hosting, database |

The metadata field is free-form key and value text. Each action's metadata has to be checked for personal data before the log is shared outside the team.

## 3. Guardian consent (`Guardian`)

| Data | Whose | Purpose | Kept today | Processor |
|---|---|---|---|---|
| Guardian: full name, e-mail, phone | Parent or guardian | Verifying consent for a candidate under 18 | Kept; no removal | Hosting, database |
| Guardian link: guardian id, candidate id, candidate e-mail, verification token, status, verified and revoked times | Guardian and candidate | Proving that the guardian has been verified for that candidate | Kept; no removal | Hosting, database |

## 4. Batches and invitations (`Batch`, `Invite`)

| Data | Whose | Purpose | Kept today | Processor |
|---|---|---|---|---|
| Batch member: e-mail, phone (optional), batch membership | Candidates on a roster | Enrolling candidates in exams | Kept; no removal | Hosting; Brevo (e-mail); Meta WhatsApp (phone) |
| Batch: name | Candidates and staff (by name) | Grouping candidates | Kept | Hosting |
| Invite: e-mail, exam id, batch member id, status, sent, accepted and declined times, created-by and accepted-by user ids | Candidates | Invitation lifecycle | Kept; no removal | Brevo (e-mail); Meta WhatsApp (phone) |
| Invite code | Candidates | Accepting an invitation | Kept | Hosting |

## 5. Consent ledger (`Consent`)

| Data | Whose | Purpose | Kept today | Processor |
|---|---|---|---|---|
| Consent record: subject id, purpose, notice version, granted and withdrawn times, who gave it | Every user who agrees to a purpose | Proof of consent (section 7.2: "store the consent record (who, when, which notice version, purpose)") | Kept; no removal | Hosting, database |
| Notice version: purpose, version, effective date, link | None (public text) | Shows which text was agreed to | Kept | None. The seeded links point to `example.invalid` until issue #13 is done |

## 6. Exam setup (`ExamAuthoring`, `QuestionBank`)

No candidate data. Exams, sections, marking schemes, proctoring profiles (configuration only), books, chapters, classes and questions hold no personal data. The one exception is the review history of a question: each review entry keeps the reviewing staff member's user id and display label, and the comment they wrote.

## 7. Attempts and candidate activity (`ExamRuntime`)

| Data | Whose | Purpose | Kept today | Processor |
|---|---|---|---|---|
| Attempt: candidate id, exam id, start, end and submit times, status, seed, score, auto-submit flag | Candidates | Running and scoring an exam | Kept with the results | Hosting, database |
| Attempt answers, marks for review, question order | Candidates | Scoring, and resuming an attempt | Kept | Hosting, database |
| Client sighting: IP address, device fingerprint, time, reason (for example "changed") | Candidates | Noticing that an attempt moved to another device or network | Kept; no removal | Hosting, database |
| Focus violation: the time the server heard that the candidate left the exam page | Candidates | Evidence for reviewers and disputes (FR-22) | Kept | Hosting, database |
| Warning: the text an administrator sent to the candidate during the attempt, who sent it, when | Candidates and staff | Showing warnings to the candidate, and disputes (FR-29) | Kept | Hosting, database |
| Dispute: candidate's reason, question id, status, rejection explanation | Candidates and staff | Answer-key disputes (FR-31) | Kept | Hosting, database |
| Issue report: candidate, attempt, exam and question ids, category, free-text message, resolution note, resolver | Candidates and staff | Reporting a problem during an exam | Kept | Hosting, database |
| Attempt request and extra-attempt grant: free-text reason, decision | Candidates and staff | Extra attempts | Kept | Hosting, database |
| Result revision: free-text reason, the staff member who changed the result | Staff | Rescoring audit | Kept | Hosting, database |
| Notification delivery: candidate's user id, kind, subject id, attempts, last try, sent time (no address is stored) | Candidates | Sending each e-mail once (FR-39) | Kept; no removal | Hosting; Brevo |
| Accommodation: candidate id, exam id, extra time, reader or scribe, alternate formats (large text, high contrast, screen reader), free-text notes | Candidates with an accommodation | Meeting RPwD Act duties (section 7.3) | Kept | Hosting, database |

**Sensitive rows:** client sightings (IP address and device identifier), focus violations, and accommodation notes. The notes can hold health or disability detail. Section 7.2 says any monitoring of a minor needs a legal opinion first (issue #11).

## 8. Processors and where data is held

| Processor | What it receives | Where | Status |
|---|---|---|---|
| Render (API and website hosting) | Every request and response; application logs | Region not set in `render.yaml` | Pin to an Indian region, or record why not |
| Neon (Postgres) | All personal data at rest | Region chosen by the operator ("the region nearest your users", `docs/deploy-render.md`) | Must be an Indian region (section 7.3). Not enforced in the repository |
| Brevo (transactional e-mail, HTTPS API; SMTP is also supported) | E-mail addresses, sign-in codes, invitations | Set by the provider; to confirm | Confirm location, and sign a data-processing agreement |
| Meta WhatsApp Cloud API (`https://graph.facebook.com` by default) | Phone numbers, sign-in codes, invitations | Set by the provider; to confirm | Used when WhatsApp delivery is configured. Cross-border transfer question for counsel |

## 9. Gaps the map exposes

1. **No retention or deletion.** No job removes users, sessions, OTP rows, client sightings, or any other row above. Section 7.3 (purpose-based retention with automated deletion) and FR-47 are not built.
2. **No data-request workflow.** Access, correction, erasure and grievance requests (FR-48, section 7.3) cannot be served from the app. There is no grievance officer contact in the product.
3. **Guardian verification token** is stored as given and compared directly. It is not hashed and is not marked single-use.
4. **Staff can reveal login and registration codes.** `OtpPurposeExtensions.IsRevealableToStaff` allows this for `Login` and `Registration`. Who may do it, and whether each reveal is logged, needs confirming.
5. **Minors' data.** Client sightings, focus violations and accommodation notes are kept for every candidate, including under-18s. Counsel's opinion (issue #11) decides whether they may be kept, and for how long, for a child.
6. **Data residency.** The repository does not pin any region. The database region is the operator's choice, and the WhatsApp and possibly the e-mail processors are outside India. Section 7.3 asks for Indian cloud regions with the region configurable.
7. **Logs.** The WhatsApp OTP sender logs a masked destination and never the code. Hosting logs and exception messages have not been checked for e-mail addresses or codes.

## 10. Not covered

- Hosting-provider logs and backups (Render and Neon internal retention).
- Browser storage beyond the language choice.
- The proctoring media features (FR-24, FR-25, FR-27), which are not built (milestone M6). Their rows are added when they are.
