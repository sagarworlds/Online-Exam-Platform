# Retention periods: a proposal for the owner to edit

**Decision:** D3 in `docs/owner-decisions.md`, answered 2026-10-15. The owner proposes a period for each row of the data map, and counsel confirms. This file is the starting point for the owner's edit.
**Issues:** [#79, FR-47 retention and deletion](https://github.com/sagarworlds/Online-Exam-Platform/issues/79), and the privacy notice text in [#269](https://github.com/sagarworlds/Online-Exam-Platform/issues/269).
**Status:** DRAFT. The periods below are engineering's starting points, built from each row's purpose in the [data map](dpdp-data-map.md). They are **not legal advice**, and no period is final until you edit it and counsel confirms it. Rows marked *Built* or *Required* come from the code or the requirements; every *Proposal* is a guess to be corrected.

## How to use this file

1. Read the "Proposed period" of each row. Change it where it is wrong for how the platform will be run. Add a note in the last column if you want one.
2. Answer the four questions under "For the owner" below.
3. Send the edited file to counsel with the brief ([counsel-brief.md](counsel-brief.md), questions 8 to 11, 15 and 18). Counsel confirms or changes each period.
4. Tell engineering. The retention job (#79) is built from the final table, and the privacy notice text (#269) quotes it.

Where the job removes a row it either deletes it or replaces the identifying fields, whichever the row needs. That is decided row by row when the job is built.

## For the owner

1. **Results history.** The proposal keeps a candidate's attempts and answers for as long as their account exists, because "my performance" and certificates show them. Do the schools and institutes that use the platform expect results to be available for longer than that, or for a fixed time such as a number of years? If so, write the number.
2. **Inactive accounts.** The proposal removes an account 24 months after it was last used. The code has no "last used" time on an account today (`User` has none), so the job needs one added. Is 24 months right?
3. **Monitoring data.** Sightings (IP address and device), focus violations and warnings are kept 90 days after results are released, which covers the 7-day dispute window and the review that follows. Is 90 days right, or should it be shorter?
4. **Proctoring media.** Not built. The requirements say "N days after result finalisation" (section 7.3). The proposal is 30 days, and only if counsel allows media at all.

## The table

Periods run from the moment named in "The clock starts".

### Identity

| Store | Personal data | Purpose | Proposed period | The clock starts | Basis | Note |
|---|---|---|---|---|---|---|
| `Users` | Name, e-mail, phone, date of birth, password hash | Account, sign-in, the age check | Removed 24 months after the account was last used, or when an erasure request is carried out | The last use of the account | Proposal | Needs a "last used" time on the account (see question 2) |
| `OtpChallenges` | Destination, code hash, revealable code | Sign-in and registration codes | 1 day | After the code expires | Built (FR-47) | The unspent code is cleared when used or superseded, not when it expires unused (G5) |
| `UserSessions` | Session token hash, IP address, device signature | Keeping a session | 1 day | After the session expires | Built (FR-47) | |
| `PasswordResetTokens` | User id, token hash | Password reset | 1 day | After the link expires | Built (FR-47) | |
| `DataRequests` | User id, kind, details, status, resolution note | Answering access, correction and erasure requests | 3 years | After the request is resolved | Proposal | Long enough to show how a request was answered if it is challenged. Counsel to confirm the period |

### Admin

| Store | Personal data | Purpose | Proposed period | The clock starts | Basis | Note |
|---|---|---|---|---|---|---|
| `AuditLogs` | Actor id and role, action, entity, free-text metadata | Accountability (FR-40), CERT-In-aligned logs (NFR-13) | 12 months, and never less than 180 days | After the entry is written | Required minimum of 180 days (`docs/observability.md`); the 12 months is a proposal | The retention job must not purge an audit row younger than 180 days. Counsel question 8 asks about the audit log |

### Batch, invitations and guardians

| Store | Personal data | Purpose | Proposed period | The clock starts | Basis | Note |
|---|---|---|---|---|---|---|
| `Batches` | Name, description | Grouping candidates | 12 months | After the batch is closed | Proposal | |
| `BatchMembers` | E-mail, phone, candidate link | Enrolling candidates, sending invitations | 12 months | After the batch is closed | Proposal | |
| `Invites` | E-mail, exam, status, times | Invitation lifecycle | 90 days | After the invitation is accepted, declined, revoked or expired | Proposal | |
| `InviteCodes` | The code, as given (not hashed, G6) | Accepting an invitation | 1 day | After the code is used, revoked or expired | Proposal | Short on purpose: an unhashed credential should not outlive its use |
| `Guardians` | Name, e-mail, phone | Verifying consent for a candidate under 18 | 12 months | After the guardian's last link ends | Proposal | |
| `GuardianLinks` | Guardian, candidate e-mail, token hash, status | Proof that a guardian was verified | 12 months | After the link ends (revoked, or the candidate turns 18) | Proposal | Counsel question 6 asks what happens to the link at 18 |

### Consent

| Store | Personal data | Purpose | Proposed period | The clock starts | Basis | Note |
|---|---|---|---|---|---|---|
| `ConsentRecords` | Subject, purpose, notice version, times | Proof of consent (FR-44) | 3 years | After consent is withdrawn or the account is removed, whichever is later | Proposal | Counsel question 9 asks whether the record may outlive the account and for how long |
| `NoticeVersions` | None (public text) | Which notice version was agreed to | Kept | | | A recorded text never changes |

### Exam content (no candidate data)

`ExamAuthoring` tables and the question bank hold no candidate data and are kept. `QuestionReviewEntries` hold staff names and comments and are kept with the question. Counsel is asked whether staff names need a limit.

### ExamRuntime

| Store | Personal data | Purpose | Proposed period | The clock starts | Basis | Note |
|---|---|---|---|---|---|---|
| `Attempts`, `AttemptQuestions`, `AttemptAnswers`, `AttemptAnswerSequences`, `AttemptMarks` | Candidate id, answers, scores, times | Results, certificates, the candidate's own history | As long as the account exists, then removed with it | The account's removal | Proposal | See question 1 |
| `AttemptResultRevisions` | Previous and new score, reason | Explaining a changed score | With the attempt | | Proposal | |
| `ExtraAttemptGrants` | Candidate, exam, reason | Explaining an extra attempt | With the attempt | | Proposal | |
| `AttemptClientSightings` | IP address, device signature | Noticing a device change (FR-26) | 90 days | After results are released | Proposal | Sensitive for candidates under 18 (OP6). Counsel question 8 |
| `AttemptFocusViolations` | Kind of violation, time | Evidence for reviewers and disputes (FR-22) | 90 days | After results are released | Proposal | Counsel question 8 |
| `AttemptWarnings` | Warning text written by staff | Warning the candidate, disputes | 90 days | After results are released | Proposal | Free text; counsel question 8 |
| `Disputes` | Candidate, question, reason, resolution | Answer-key disputes (FR-31) | 12 months | After the dispute is resolved | Proposal | |
| `IssueReports` | Candidate, message, resolution | Problems reported during an exam (FR-42) | 12 months | After the report is resolved | Proposal | Counsel question 8 |
| `AttemptRequests` | Candidate, message, decision note | Requests for another attempt | 12 months | After the request is decided | Proposal | |
| `Accommodations` | Extra time, reader or scribe flag, formats, **notes** | Meeting the duties under the RPwD Act (FR-49) | Notes: 90 days. The rest: with the attempt | After results are released | Proposal | Notes can reveal a disability. Each attempt also keeps a copy of the accommodation (data map, 3.9). Counsel question 11 asks what it may hold |
| `NotificationDeliveries` | Kind, subject id, recipient id, tries | Sending each e-mail once (FR-39) | 90 days | After the e-mail is sent | Proposal | No address is stored |

### Notifications

| Store | Personal data | Purpose | Proposed period | The clock starts | Basis | Note |
|---|---|---|---|---|---|---|
| `InAppNotifications` | Recipient id, kind, subject id, exam name | The in-app feed (FR-39) | 90 days after it is read, or 12 months after it was created if never read | Read, or created | Proposal | |

### Outside the platform's own tables

| Where | What | Proposed period | Basis | Note |
|---|---|---|---|---|
| Logs | Request and application logs | 180 days, in an India-hosted store | Required minimum (NFR-13, D14) | Counsel question 15 asks whether the 180-day rule applies to application logs and whether they must stay in India. See [log-store-options.md](log-store-options.md) |
| Brevo and operator SMTP | E-mails sent, with codes and scores | The provider's own retention | Not controlled by the platform | Ask each provider for the shortest retention its plan allows, and record it here |
| Meta WhatsApp | Messages and delivery data | The provider's own retention | Not controlled by the platform | Off for now (D6) |
| Browser storage | Sign-in token, the visited-question list for each attempt, display settings | The token expires. The visited list is removed when the attempt is submitted | Proposal | The app does not remove the visited list today (G9), so this is a change to the web app |
| Proctoring media | Not built | 30 days | Proposal | Only if counsel allows media at all (counsel questions 3 and 10) |

## What is not decided here

- Whether any period must be longer because a law requires the data to be kept. Counsel is asked in questions 8 to 10.
- How a removal is carried out for rows that other rows refer to, such as an attempt that a dispute refers to. The job's design settles that.
- Backups: a removed row stays in backups until they expire. The backup window belongs to the hosting plan (D13) and has to be named in the privacy notice.
