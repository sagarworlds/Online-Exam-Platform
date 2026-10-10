# DPDP data map

**Milestone:** M0 Discovery & legal, issue [#12](https://github.com/sagarworlds/Online-Exam-Platform/issues/12).

**Status:** DRAFT for counsel review. This is an engineering inventory of what the code stores and sends. It is not legal advice. Purposes, retention periods, the grievance contact and processor locations need the owner's and counsel's decisions before this map is final.

**Source:** the code on `main` at `ac5e663`, read module by module, plus `render.yaml`, the workflows and the public privacy page (`apps/web/public/privacy.html`). Requirements: section 7 (India compliance), section 8 (proctoring), section 10 (data model), FR-43 to FR-52, NFR-6, NFR-12 and NFR-13.

**Replaces:** the earlier draft from `4bedf1b`. Section 6 lists what changed.

## Open points

Each open point needs a decision. The pull request lists the same points.

| # | Open point | Who decides |
|---|---|---|
| OP1 | Retention period for every row. No period is set in the code, the requirements or the public privacy page. Every row says "To be decided by the owner". | Owner, counsel |
| OP2 | Grievance officer name, e-mail and postal address (FR-48; section 7.3). Not in the repo. The contact section of `apps/web/public/privacy.html` still has placeholders. | Owner |
| OP3 | Processor locations: the Render region (not set in `render.yaml`), the Neon region (the operator's choice), Brevo's storage, and Meta's local-storage setting for the Indian number. The web sources behind the earlier draft could not be opened, and in this session the Brevo, Render and Neon sites could not be reached either. Treat every location claim in section 4 as unverified. | Owner, counsel |
| OP4 | Data-processing agreements with Render, Neon, Brevo and Meta, and with the operator's SMTP provider if one is used. None is in the repo. | Owner, counsel |
| OP5 | Cross-border transfer. NFR-12 says it is off by default. Several processors hold data outside India (section 4). The exception is recorded separately in `docs/compliance/nfr-12-exception.md`. | Counsel |
| OP6 | Candidates under 18. Client sightings, focus violations and accommodation notes are kept for every candidate, including children. Counsel's opinion (issue #11) decides whether this is allowed, and for how long. | Counsel |
| OP7 | Guardian consent before exam access (FR-43) is not enforced in code (G3). The consent notices must not be published until it is enforced, or their text changes. | Owner |
| OP8 | Full date of birth is stored (`Users`). Decide whether an age band is enough (data minimisation). | Owner, counsel |
| OP9 | Staff may list unspent login and registration codes (`IsRevealableToStaff`). Decide who may do it. Each read is audited, but the audit entry does not record which code. | Owner |
| OP10 | Candidate passwords. The public privacy page says "a password if you use one", and the password-reset handler sets a hash on any account. Confirm whether candidates can hold a password. | Owner |
| OP11 | The public privacy page makes retention, deletion and processor statements that this map does not back (G14). Reconcile the page with sections 3 to 5. | Counsel, owner |
| OP12 | FR-50 asks the roster file to carry a name and, for minors, a guardian contact. The code reads an e-mail and an optional phone only (section 3.3). Decide which one is right. | Owner |

## 1. Summary

| Module | Personal data held | Whose | Sent outside the platform to | Retention today | Main concern |
|---|---|---|---|---|---|
| Identity | Name, e-mail, phone, exact date of birth, password hash (if set), sign-in codes, session IP address and device signature | Candidates, staff | Meta (phone codes); Brevo or SMTP (e-mail codes) | Kept; no removal | Plain-text login codes (G5); full date of birth (OP8) |
| Admin | Actor id and role, action, free-text metadata | Staff; candidates as actors | Nothing | Kept; no purge | Free-text metadata (G10) |
| Batch | E-mail, phone, candidate link, registration times | Candidates | Brevo or SMTP; Meta (if phone invitations are on) | Soft-deleted only; kept | Roster lacks name and guardian contact (OP12) |
| Invite | E-mail, invitation codes (stored unhashed), invitation status | Candidates | Brevo or SMTP; Meta (if on) | Soft-deleted only; kept | Codes not hashed (G6) |
| Guardian | Guardian name, e-mail and phone; candidate e-mail; hashed verification token | Guardians; candidates under 18 | Brevo or SMTP | Kept; no removal | Consent not enforced (G3) |
| Consent | Consent grants and withdrawals, with the notice version | Candidates; guardians | Nothing | Never deleted | Ledger not enforced (G3) |
| ExamAuthoring | None (exam configuration) | None | Nothing | Kept | None found |
| QuestionBank | Staff names and comments in review history; question content | Staff | Nothing | Kept | Free text (G11) |
| ExamRuntime | Answers, scores, warnings, focus violations, IP and device sightings, accommodations, disputes, requests, e-mail log | Candidates; staff | Brevo or SMTP (exam e-mails, which include scores) | Kept; no removal | Minors' monitoring data (OP6, G4) |
| Notifications | In-app feed: account id, notice kind, exam name, read time | Candidates; staff | Nothing | Kept; no removal | Feed rows never removed (G1) |
| Cross-cutting (SharedKernel, Host, web app) | Mail and WhatsApp payloads, WhatsApp delivery webhooks, logs, browser storage, device signature | All of the above | Brevo or SMTP; Meta; GitHub (no personal data) | Not set | Logs not checked (G12); browser storage (G9) |

## 2. How to read the tables

- **Personal data.** A field is personal data when it identifies a person or relates to one. Ids and hashes that link to a person count. "Sensitive" means health or disability-related, or about a child. The DPDP categories are for counsel to assign.
- **Retention.** "To be decided by the owner" where no period is set. "Current behaviour" describes what the code does. Nothing in the code removes personal data automatically (G1).
- **Processor.** The outside party that receives the data. "Hosting" means Render (API and web) and Neon (database), as in section 4. "None" means the data stays in the platform's database.
- **Sources.** Each module section names the folder read. Paths are under `apps/api/src/Modules/` unless stated.

## 3. Modules

### 3.1 Identity (`Identity`)

Source: `Identity/ExamPlatform.Modules.Identity.Domain/` (User, OtpChallenge, UserSession, PasswordResetToken, Role, Permission) and `Identity/ExamPlatform.Modules.Identity.Infrastructure/IdentityDbContext.cs`.

| Store | Personal data held | Whose | Purpose | Retention | Current behaviour | Processor |
|---|---|---|---|---|---|---|
| `Users` | E-mail (optional), phone (optional), display name, exact date of birth, password hash (only if set), status, role links | Candidates and staff | Account and sign-in; the age check that guardian consent depends on (FR-43) | To be decided by the owner | Kept; no removal job. Date of birth is stored in full, not as an age band (OP8). The age band is computed in code and used only by unit tests (G3) | Hosting |
| `OtpChallenges` | Destination (e-mail or phone), code hash, revealable code (login and registration only), purpose, attempt count, expiry, consumed and superseded times | Anyone signing in or registering | Sign-in and registration codes | To be decided by the owner | Rows stay after use or expiry. The revealable code is cleared when the code is used or superseded, but not when it expires unused (G5). Staff can list unspent login and registration codes, and the read is audited (OP9) | Hosting; Brevo or SMTP (e-mail); Meta WhatsApp (phone) |
| `UserSessions` | Session token hash, IP address, device signature, issued, expiry and revoked times, revoke reason | Signed-in users | Keeping a session; one active session per candidate; noticing a second device (FR-26) | To be decided by the owner | Rows stay after expiry or revocation | Hosting |
| `PasswordResetTokens` | User id, token hash, expiry, consumed and revoked times | Account holders who reset a password | Password reset | To be decided by the owner | Rows stay after use. No outbound message for this table was found in the code read | Hosting |
| `Roles`, `Permissions`, `UserRoles`, `RolePermissions` | No personal data. They link a staff user id to roles and permissions | Staff | Access control | To be decided by the owner | Kept | Hosting |

**Lookups for other modules.** `IContactDirectory` returns the account id for an e-mail address, and the phone number of an active account that holds that address. `IStaffDirectory` returns the e-mail addresses of active users who hold a permission. Both are read through the Contracts project only. Callers: Invite (section 3.4) and ExamRuntime, for the staff e-mail about attempt requests (section 3.9).

### 3.2 Admin (`Admin`)

Source: `Admin/ExamPlatform.Modules.Admin.Domain/AuditLog.cs` and `Admin/ExamPlatform.Modules.Admin.Infrastructure/AdminDbContext.cs`.

| Store | Personal data held | Whose | Purpose | Retention | Current behaviour | Processor |
|---|---|---|---|---|---|---|
| `AuditLogs` | Time, actor user id, actor role, action, entity type and id, metadata (free text: reasons, warning text, exam and batch names, ids), correlation id | Staff and candidates who act in the system | Accountability (FR-40); CERT-In-aligned logs (NFR-13) | To be decided by the owner. NFR-13 and section 7.3 set 180 days for logs in India; whether that covers audit rows is for the owner to decide | Kept; no purge job. The metadata keys in the code are ids, reasons, messages and names. No e-mail or phone key was found. The WhatsApp test entry keeps a masked number and the message length, not the text | Hosting |

### 3.3 Batch (`Batch`)

Source: `Batch/ExamPlatform.Modules.Batch.Domain/` (Batch, BatchMember, MemberContact, RosterValidator).

| Store | Personal data held | Whose | Purpose | Retention | Current behaviour | Processor |
|---|---|---|---|---|---|---|
| `Batches` | Name (free text, which could name a person), description, status, maximum members, created-by user id, exam id | Staff who create batches | Grouping candidates for an exam (FR-50) | To be decided by the owner | Soft-deleted only (`IsDeleted`); rows kept | Hosting |
| `BatchMembers` | E-mail (trimmed and lower-cased), phone (optional, spaces and dashes removed), candidate user id once registered, registration status, invite-sent and registration-completed times, soft-delete flag | Candidates on a roster | Enrolling candidates; sending invitations | To be decided by the owner | Soft-deleted only; kept. One active member per e-mail per batch | Hosting; Brevo or SMTP (invitation e-mail); Meta WhatsApp (phone, when invitations on WhatsApp are on) |
| Roster import (no table) | E-mail and optional phone for each row, read by the roster validator | Candidates | Creating batch members | Not kept beyond the batch member rows | Reads an e-mail and an optional phone only. It does not read a name or a guardian contact (OP12). No table holds the uploaded file | None |

### 3.4 Invite (`Invite`)

Source: `Invite/ExamPlatform.Modules.Invite.Domain/` (Invite, InviteCode) and `Invite/ExamPlatform.Modules.Invite.Application/Commands/InviteHandlers.cs`.

| Store | Personal data held | Whose | Purpose | Retention | Current behaviour | Processor |
|---|---|---|---|---|---|---|
| `Invites` | E-mail, exam id, batch member id (optional), status, sent, accepted and declined times, created-by and accepted-by user ids, soft-delete flag | Candidates | Invitation lifecycle (FR-50a) | To be decided by the owner | Soft-deleted only; kept | Hosting; Brevo or SMTP (invitation e-mail); Meta WhatsApp (phone, if on) |
| `InviteCodes` | Code, stored as given and not hashed (G6); invite id; expiry, used and revoked times | Candidates | Accepting an invitation | To be decided by the owner | Kept. The code is inside the invitation link and in any WhatsApp message. The invitation e-mail gives the link and its expiry | Hosting; Brevo or SMTP; Meta WhatsApp |

**Lookups (no table).** When the invited address belongs to an active account, Invite asks Identity for that account's id, to send an in-app notice (section 3.10). When WhatsApp invitations are on, it asks for the phone number on that account (`FindPhoneNumberByEmailAsync`). The phone number is then used for the WhatsApp message. Processor: Meta WhatsApp.

### 3.5 Guardian (`Guardian`)

Source: `Guardian/ExamPlatform.Modules.Guardian.Domain/` (Guardian, GuardianLink, GuardianLinkToken) and `Guardian/ExamPlatform.Modules.Guardian.Application/Commands/GuardianHandlers.cs`.

| Store | Personal data held | Whose | Purpose | Retention | Current behaviour | Processor |
|---|---|---|---|---|---|---|
| `Guardians` | Full name, e-mail, phone (optional), created and updated times, soft-delete flag | Parents or guardians of candidates | Verifying consent for a candidate under 18 (FR-43, FR-45) | To be decided by the owner | Kept; soft-delete only | Hosting |
| `GuardianLinks` | Guardian id, candidate id, candidate e-mail, verification token hash (SHA-256, hex), token expiry, status (pending, verified or revoked), verified and revoked times | Guardians and candidates | Proof that a guardian was verified for a candidate | To be decided by the owner | Only the hash is kept. A link can be verified only while pending, so a token works once. The raw token goes out by e-mail and is not kept. This resolves the earlier gap | Hosting |
| Consent request e-mail (no table) | To the guardian: guardian's name and e-mail, the candidate's e-mail, the confirm link (carrying the raw token), the expiry | Guardians and candidates | Asking the guardian to confirm | Not kept by the platform beyond the mail server | Sent by `SmtpGuardianConsentNotifier`. The verified event (`GuardianLinkVerifiedEvent`) is raised, but no handler consumes it (G3) | Brevo or SMTP |

### 3.6 Consent (`Consent`)

Source: `Consent/ExamPlatform.Modules.Consent.Domain/` (ConsentRecord, NoticeVersion) and `Consent/ExamPlatform.Modules.Consent.Infrastructure/ConsentSeeder.cs`.

| Store | Personal data held | Whose | Purpose | Retention | Current behaviour | Processor |
|---|---|---|---|---|---|---|
| `ConsentRecords` | Subject id, purpose (`TermsOfService`, `PrivacyNotice` or `ProctoringDataProcessing`), notice version id, granted and withdrawn times, given-by id, withdrawn-by id | Candidates; a guardian when they give consent for a minor (given-by) | Proof of consent (FR-44; section 7.2) | To be decided by the owner | Never deleted. Withdrawal sets a time and keeps the grant. The web consent page is the only caller; nothing else reads the ledger (G3) | Hosting |
| `NoticeVersions` | Purpose, version label (for example `v1`), effective-from time, content reference (a URL or hash, not the text) | None (public text) | Shows which version of a notice was agreed to | To be decided by the owner | Three rows are seeded on first start, each pointing at `https://example.invalid/legal/...-v1`. The ledger holds no notice text and no language | Hosting |

### 3.7 ExamAuthoring (`ExamAuthoring`)

Source: `ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/` (Exam, ExamSection, SectionDrawRule).

| Store | Personal data held | Whose | Purpose | Retention | Current behaviour | Processor |
|---|---|---|---|---|---|---|
| `Exams`, `ExamSections`, `ExamQuestions`, `SectionDrawRules` | Exam name and description, series, schedule, sections and time limits, question picks, draw rules (book, chapter, difficulty, topic) | None. No creator field was found in these tables | Configuring exams | To be decided by the owner | Kept | Hosting |

### 3.8 QuestionBank (`QuestionBank`)

Source: `QuestionBank/ExamPlatform.Modules.QuestionBank.Domain/` (Question, QuestionVersion, QuestionReviewEntry, Book, Chapter, SchoolClass).

| Store | Personal data held | Whose | Purpose | Retention | Current behaviour | Processor |
|---|---|---|---|---|---|---|
| `Questions`, `QuestionVersions`, `QuestionOptions`, `Books`, `Chapters`, `Classes` | Question content (text, options, key, explanation); created-by staff user id. Pictures added in the rich-text editor are stored inside the question content | Staff authors. Candidates do not appear here | Question content | To be decided by the owner | Kept. Pictures are limited per question and stored with the content, not as separate files. The question import creates rows; no table holds the uploaded file | Hosting |
| `QuestionReviewEntries` | Question id, review kind, reviewer user id, reviewer display label (a name), comment (free text), version number, status after, created time | Staff reviewers | Review history of a question | To be decided by the owner | Kept | Hosting |

### 3.9 ExamRuntime (`ExamRuntime`)

Source: `ExamRuntime/ExamPlatform.Modules.ExamRuntime.Domain/` and `ExamRuntime/ExamPlatform.Modules.ExamRuntime.Infrastructure/ExamRuntimeDbContext.cs`.

| Store | Personal data held | Whose | Purpose | Retention | Current behaviour | Processor |
|---|---|---|---|---|---|---|
| `Attempts` | Candidate user id, exam id, attempt number, start, deadline, pause, submit and end times, status, auto-submit and ended-by-violations flags, acknowledged notice text (free text), accommodation copy, score and max score, staff ids and reasons for termination or invalidation | Candidates; staff who moderated | Running, timing and scoring an attempt | To be decided by the owner | Kept | Hosting |
| `AttemptQuestions`, `AttemptAnswers`, `AttemptMarks` | Question order, selected option ids, answer text, answer time, questions marked for review, time marked | Candidates (their answers) | Scoring; saving answers and resuming (NFR-4) | To be decided by the owner | Kept. Candidate answers are personal data and are kept with the results | Hosting |
| `AttemptClientSightings` | IP address, device signature, time seen, reason (for example, the device changed) | Candidates | Noticing that an attempt moved to another device or network (FR-26) | To be decided by the owner | Kept; no removal. Sensitive for minors (OP6, G4) | Hosting |
| `AttemptFocusViolations` | Kind of violation (for example, leaving the exam page or full screen), time | Candidates | Evidence for reviewers and disputes (FR-22) | To be decided by the owner | Kept. Sensitive for minors (OP6, G4) | Hosting |
| `AttemptWarnings` | Warning text (free text written by staff), staff user id, time | Candidates and staff | Warning the candidate during an exam; disputes (FR-29) | To be decided by the owner | Kept | Hosting |
| `AttemptResultRevisions` | Previous and new score and max score, reason (free text), revision time | Candidates | Recording score revisions, which the score-revised e-mail reports (FR-39) | To be decided by the owner | Kept. The table has no staff id field; who made the change is not recorded here | Hosting |
| `Disputes` | Candidate id, exam id, question id, reason (free text), status, resolver user id, resolution note, times | Candidates and staff | Answer-key disputes (FR-31) | To be decided by the owner | Kept | Hosting |
| `IssueReports` | Candidate id, attempt, exam and question ids, category, message (free text), status, resolver, resolution note | Candidates and staff | Reporting a problem during an exam (FR-42) | To be decided by the owner | Kept | Hosting |
| `AttemptRequests` | Candidate id, exam id, message (free text), status, decided-by staff id, decision note, times | Candidates and staff | Requests for another attempt | To be decided by the owner | Kept | Hosting; Brevo or SMTP (e-mails below) |
| `ExtraAttemptGrants` | Candidate id, exam id, grant number, granted-by staff id, time, reason (free text) | Candidates and staff | Extra attempts | To be decided by the owner | Kept | Hosting |
| `Accommodations` | Candidate id, exam id, extra time, reader or scribe flag, alternate formats (for example, large text, high contrast, screen reader), notes (free text), updated-by staff id and time | Candidates with an accommodation | Meeting RPwD Act duties (section 7.3; FR-49) | To be decided by the owner | Kept. Notes can reveal a disability or other need. Sensitive | Hosting |
| `NotificationDeliveries` | Kind of e-mail, subject id (exam, attempt or revision), recipient (candidate user id), tries, last try time, sent time. No address is stored | Candidates | Sending each e-mail once (FR-39) | To be decided by the owner | Kept; no removal | Hosting |

**E-mails sent by ExamRuntime.** Each one goes through Brevo or SMTP (section 4). The scheduled run is started by the API timer and by a GitHub workflow that calls `POST /v1/notifications/run` every ten minutes with a key. The workflow sends no personal data.

| E-mail | Personal data in it | Recipient |
|---|---|---|
| Exam reminder (24 hours and 1 hour before) | Candidate's e-mail, exam name, start time | Candidate |
| Result released | Candidate's e-mail, exam name, attempt number | Candidate |
| Score revised | Candidate's e-mail, exam name, previous and new score and max score, reason | Candidate |
| Attempt request decided | Candidate's e-mail, exam name, approved or declined, staff note | Candidate |
| New attempt request | Staff e-mail, exam name, the candidate's invited e-mail, the candidate's message | Staff (found through `IStaffDirectory`) |

The same events also create in-app notices (section 3.10).

### 3.10 Notifications (`Notifications`)

New since the earlier draft. Source: `Notifications/ExamPlatform.Modules.Notifications.Domain/InAppNotification.cs`, `Notifications/ExamPlatform.Modules.Notifications.Infrastructure/NotificationsDbContext.cs` and `Notifications/ExamPlatform.Modules.Notifications.Application/InAppNotifier.cs`.

| Store | Personal data held | Whose | Purpose | Retention | Current behaviour | Processor |
|---|---|---|---|---|---|---|
| `InAppNotifications` | Recipient account id; notice kind (invite received, attempt request received or decided, dispute rejected, exam reminder, result released, score revised); subject id (the invite, attempt, request, dispute, exam or revision); exam name as it was at the time (free text); created time; read time | Account holders: candidates and staff | In-app feed (FR-39). One notice per event and recipient | To be decided by the owner | Kept; no removal job. The read time is set once and kept. Score values are not in the row. Guardians have no account and get no notices | Hosting |

### 3.11 Cross-cutting: SharedKernel, Host and the web app

These are not modules, but they send or receive personal data. Sources: `apps/api/src/SharedKernel/`, `apps/api/src/Host/ExamPlatform.Api/`, `apps/web/src/app/`, `render.yaml` and `.github/workflows/`.

| Area | Personal data | Sent to | Retention | Notes |
|---|---|---|---|---|
| Mail (SharedKernel `Email/`) | Recipient, sender, subject and body of every e-mail in sections 3.1 to 3.9 | Brevo's HTTPS API (`https://api.brevo.com/v3/smtp/email`) when `Mail__Provider` is `BrevoApi`; otherwise the operator's SMTP server | To be decided by the owner (OP1, OP3, OP4) | The body of each message was read only as far as the personal data listed in sections 3.1 to 3.9 |
| SMS (SharedKernel `Sms/`) | None | Nothing: no SMS provider is built in | Not kept | With `Sms:Enabled` set to true, each attempt is logged as not sent. The log carries no number |
| WhatsApp, outbound (SharedKernel `WhatsApp/WhatsAppCloudApiSender.cs`) | Recipient phone (normalised); message parameters: sign-in code, invitation code, link, exam name, expiry; admin test text | Meta WhatsApp Cloud API, at `https://graph.facebook.com` by default (`WhatsAppOptions.BaseUrl`) | Meta's own retention (unverified, OP3) | Admin test messages can be sent to any number the administrator types. The audit entry keeps the masked number |
| WhatsApp, delivery webhook (Host `WhatsApp/WhatsAppWebhookEndpoints.cs`) | Meta posts delivery statuses (message id, recipient, status, error code) and inbound messages (sender and type). Inbound content is not read | Not sent onward | Delivery statuses are held in memory for 24 hours, up to a capacity limit (`InMemoryWhatsAppDeliveryTracker`). They are not in the database and are lost when the process restarts | Logs carry masked numbers only |
| Logs (Host and all modules) | Contact details appear only in masked form where the code masks them (`ContactMasker`, `WhatsAppPhoneNumber.Mask`) | Render keeps container logs; its retention is unverified (OP3) | To be decided by the owner. NFR-13 asks for 180 days in India; no log sink or retention is configured in the repo | Exception messages and request logs were not checked for e-mail addresses or codes (G12) |
| Device signature (web `shared/device/device-signature.ts`) | A hash of the browser's user agent, language, screen size, colour depth, time zone and processor count, sent with each API request | Render and Neon, through the API | As for the rows it is stored in (sections 3.1 and 3.9) | Coarse by design. The code comment says it is evidence for a person to weigh, not an identity (FR-26) |
| GitHub Actions (`.github/workflows/notifications.yml`) | None. The workflow calls the notification run with a key | GitHub | GitHub's retention (not checked) | Workflow logs were not checked for personal data |

**Browser storage (web app).** Everything below stays on the candidate's or staff member's device.

| Key | Where | What it holds | Set by | Cleared by the app |
|---|---|---|---|---|
| `exam-platform.auth-token` | localStorage | The sign-in JWT: user id, e-mail (if set), roles, permissions, session id | Sign-in | Removed when the stored token is found expired. Removal on sign-out was not checked |
| `exam-platform.language` | localStorage | The chosen interface language (English, Hindi or Marathi) | Language switcher | No |
| `exam-platform.admin-sidebar-collapsed` | localStorage | Whether the admin sidebar is collapsed | Admin sidebar | No |
| `exam.lowBandwidth`, `exam.textZoom`, `exam.highContrast` | localStorage | Exam display and bandwidth settings | Candidate exam screen | No |
| `exam.visited.<attemptId>` | localStorage | The questions the candidate has visited in that attempt | Candidate exam screen | No. One key per attempt. The app does not delete it (G9) |
| Dismissed warnings for an attempt | sessionStorage | Which warnings the candidate has dismissed | Candidate exam screen | When the browser tab closes |

## 4. Processors

Location facts in this section are unverified. The Brevo, Render and Neon sites could not be reached from the drafting environment (DNS lookups failed), and the earlier draft's sources could not be opened. Confirm each fact in the provider's console or contract before relying on it.

| Processor | What it receives | Location, as stated in the repo | Verified? | Open points |
|---|---|---|---|---|
| Render (API and web hosting; `render.yaml`) | Every API request and response; container logs; secrets through environment variables | `render.yaml` has no region key, so Render's default applies. The earlier draft's region list came from a search summary and was not checked | No | OP3, OP4, G7 |
| Neon (Postgres; `docs/deploy-render.md`) | All personal data at rest | Chosen by the operator ("the region nearest your users"). The repo does not enforce it. Section 7.3 asks for an India region, or the exception must be recorded in the NFR-12 note | No | OP3, OP5, G7 |
| Brevo (transactional e-mail; `Mail__Provider=BrevoApi`) | Recipient e-mail, sender, subject and body: sign-in codes, invitation links with codes, guardian consent requests, exam e-mails with scores | The earlier draft says EU storage, from a search summary. Not checked | No | OP3, OP4 |
| Operator SMTP (`Mail__Provider=Smtp`) | The same messages | Chosen by the operator | Not applicable | OP4 |
| Meta WhatsApp Cloud API (`WhatsApp__*`, `Invite__WhatsApp__TemplateName`) | Recipient phone (normalised); sign-in codes, invitation codes, links, exam names, expiry, admin test text. Meta also receives delivery information | Default `https://graph.facebook.com`. The earlier draft says US by default, with local storage available for the Indian number. Not checked. The public privacy page says Meta "may process data outside India" | No | OP3, OP4, OP5 |
| GitHub (source, CI, scheduled trigger) | Nothing personal. The trigger carries a key only | Not applicable | Not applicable | None |

The public privacy page names Meta, and calls the others "hosting and database providers, email delivery". It does not name Render, Neon or Brevo (G14).

## 5. Gaps the map exposes

1. **No retention or deletion (G1).** No job removes users, sessions, codes, sightings, grants, feed rows or anything else in section 3. Expiry checks reject expired codes and sessions but never delete them. There is no hosted background job in the API. FR-47 and section 7.3 are not built.
2. **No data-request workflow (G2).** Access, correction and erasure requests (FR-48) cannot be served from the app. There is no SLA tracking and no grievance contact in the product.
3. **Under-18 gating is not enforced (G3; FR-43).** No code outside the Consent module calls the consent service. `User.GetAgeBand` is used only by unit tests. `GuardianLinkVerifiedEvent` is raised, but no handler consumes it. A candidate under 18 can register, be invited and sit an exam without any guardian consent on record.
4. **Minors' monitoring data (G4).** Client sightings (IP address and device signature), focus violations and accommodation notes are kept for every candidate. Section 7.2 asks for a legal opinion before monitoring minors (OP6). The per-profile policy in section 8 is not built, so these records are written for every candidate, children included.
5. **Login and registration codes in plain text (G5).** `OtpChallenges.RevealableCode` holds the code in plain text until the challenge is used or superseded. An expired, unused code keeps it. Staff can list these codes (OP9).
6. **Invitation codes are not hashed (G6).** `InviteCodes.Code` is stored as given. The same value is sent in the link and in WhatsApp messages.
7. **Data residency is not pinned (G7).** `render.yaml` sets no region. The Neon region is the operator's choice. Processor locations are unverified (section 4; OP3, OP5). The NFR-12 exception note covers the current providers.
8. **Full date of birth (G8).** `Users` stores the exact date of birth. The privacy page says it is used to tell whether a candidate is under 18. An age band would meet that purpose (OP8).
9. **Browser storage (G9).** The sign-in token, which contains the e-mail, stays in localStorage. The visited-question list for each attempt stays in localStorage, and the app does not delete it. The privacy page describes both under "On your device", but it does not say how long they remain.
10. **Audit metadata (G10).** Metadata is free text, and no purge job exists. Reasons, warning text and names of exams and batches are kept for as long as the log is kept.
11. **Free text may hold personal data about other people (G11).** Warnings, dispute reasons, issue messages, attempt request messages, accommodation notes, review comments and grant reasons are free text. Nothing redacts or reviews them.
12. **Logs (G12).** Masking covers OTP and WhatsApp contact details. Not checked: exception messages and request logs for e-mail addresses or codes. No log sink or retention is set in the repo, and NFR-13 (180 days in India) is not implemented.
13. **Roster import (G13).** FR-50 asks for a name and, for minors, a guardian contact. The code reads neither (OP12).
14. **Public privacy page (G14).** `apps/web/public/privacy.html` says personal data is deleted once it is no longer needed (its section 7). It also has placeholders for the operator and grievance contact, and does not name Render, Neon or Brevo. Its statements must match this map (OP11).
15. **Language coverage (G15).** The interface has English, Hindi and Marathi. The consent notices in the separate consent PR cover English and Hindi only.

## 6. Corrections from the earlier draft (`4bedf1b`)

- **Guardian verification token:** now stored as a SHA-256 hash and works once. The earlier gap about an unhashed token is resolved.
- **Browser storage:** the earlier draft said the browser keeps only the language. That was wrong. It also keeps the sign-in token, the visited-question list, display and bandwidth settings, and dismissed warnings for an attempt (section 3.11).
- **WhatsApp:** the earlier draft covered sign-in codes and invitations. The code also sends admin test messages, and it takes delivery-status webhooks from Meta.
- **Notifications:** the in-app feed is a new module (section 3.10).
- **E-mail transport:** the earlier draft described SMTP as "also supported". In the code, Brevo's API is used when `Mail__Provider` is `BrevoApi`, and SMTP otherwise.
- **Audit metadata:** confirmed as free text. No e-mail or phone key was found.
- **Staff-visible codes:** the earlier draft's gap about staff reading codes stands (G5, OP9).

## 7. Not covered

- Hosting-provider logs, backups and internal retention (Render, Neon, GitHub).
- Brevo, Meta and operator SMTP retention.
- Proctoring media (FR-24, FR-25, FR-27). These are not built (milestone M6).
- Modules named in section 11 of the requirements that have no folder in `apps/api/src/Modules` at `ac5e663`: Proctoring, Evaluation and Ranking, and Analytics and Reporting. Their rows are added when they exist.
- Personal data of staff beyond the account rows in section 3.1. The code shows no HR records.
- Anything the browser keeps beyond the keys in section 3.11.
