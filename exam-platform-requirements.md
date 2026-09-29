# Online Exam Platform (MTG-style): Requirements & Implementation Plan

> Audience: Claude Code and the engineering team.
> Status: v1 requirements, invite-only, India-first.
> Compliance notes are engineering guidance, not legal advice. Have counsel confirm before build.

---

## 0. How to Use This File (Claude Code)

1. Read sections 1 and 2 first. They are hard rules for every task.
2. Work milestone by milestone (section 15). Do not start a milestone until the previous one's acceptance criteria pass.
3. Before choosing a tech stack, write an ADR (`docs/adr/0001-stack.md`) proposing the stack and wait for confirmation. Section 11 is a suggestion, not a decision.
4. Ask for clarification only where this file is silent and the choice is hard to reverse (schema, public API, consent handling). Otherwise state the assumption in the PR description and proceed.
5. Requirement IDs (FR-x, NFR-x) must appear in commit messages or PR descriptions of the work that implements them.

---

## 1. Working Agreements (Non-Negotiable)

### 1.1 Git
- **Branch first.** Before any code change, create a new branch from the main/development branch.
- **Branch names start with `SP/`**, followed by a type and a descriptive slug:
  - `SP/feature/exam-runtime-autosave`
  - `SP/bugfix/timer-drift`
  - `SP/chore/ci-pipeline`
- **Never add a co-author trailer** to commits (no `Co-authored-by:` lines, including any AI-attribution trailers).
- Small, focused commits. One logical change per commit.

### 1.2 Code principles
- **SOLID**, strictly:
  - *SRP*: each class, module, or function has one reason to change.
  - *OCP*: extend through new implementations, not by editing core logic.
  - *LSP*: subtypes must be substitutable for their base types.
  - *ISP*: small, role-specific interfaces.
  - *DIP*: depend on abstractions (interfaces), inject concretions.
- **Error handling:** no silent failures. Catch specific exceptions, log with context (attempt ID, user ID, request ID), and raise clean, actionable errors or return structured error states. No bare `catch`/`except`.
- **Clean code:** readability over cleverness; small, focused, pure functions where possible.

### 1.3 Documentation
- **Docstrings** on every public class, function, and interface: parameters, return values, raised exceptions.
- **Inline comments explain why**, not what. Required for scoring formulas, marking rules, timer logic, rank/percentile math, and consent rules. Do not state the obvious.

---

## 2. Product Overview

### 2.1 Goal
A reliable, timed, auto-graded online exam platform for **school and competitive-exam preparation**: mock tests, chapter tests, and contests, with large question banks, section-wise scoring, instant results, ranking, and analytics.

### 2.2 Decisions already made
| Topic | Decision |
|---|---|
| Audience | School / competitive prep, MCQ-heavy |
| Scale | Launch modest, **design for a much larger concurrent base** (section 9) |
| Proctoring | **Configurable per exam** (section 8) |
| Access model | **Invite-only** for v1; payments deferred |
| Region | **India**: DPDP Act compliance, India-region hosting |

### 2.3 In scope (v1)
MCQ-based exams (single, multi, numeric), sections, timers, autosave, results, rank/percentile, solutions, disputes, analytics, batches and invites, guardian consent for minors, configurable proctoring, accommodations.

### 2.4 Out of scope (v1)
Payments/catalog, live classes, AI grading of subjective answers, native mobile apps (responsive web/PWA only).

---

## 3. User Roles

| Role | Key capabilities |
|---|---|
| Candidate | Accept invite, take exams, view results and analytics |
| Guardian | Give, view, and withdraw consent for a minor candidate |
| Content Author | Create/edit questions, tag by topic and difficulty |
| Reviewer | Approve or reject questions |
| Exam Admin | Compose, schedule, publish exams; manage batches and invites |
| Proctor | Monitor sessions, review flagged events |
| Super Admin | Users, roles, settings, audit logs |
| Institute / Teacher | Manage a batch, view batch reports |

---

## 4. Content Hierarchy

```
Exam Series
 └── Exam
      └── Section
           └── Question Group (optional passage/case)
                └── Question (immutable Version)
                     └── Options / Answer key / Solution
```

**Question types (v1):** single-correct MCQ, multi-correct MCQ (with partial marking), numeric/integer, true/false, match-the-following, passage-based.
**Phase 2:** short subjective with manual grading.

**Question metadata:** subject, chapter, topic, difficulty, expected time, language, marks, negative marks, explanation (text/image/video), version, status (`draft → review → approved → retired`).

**Key rule:** an exam references **immutable question versions**. Edits create a new version and never alter historical attempts.

---

## 5. Functional Requirements

Tags: **[MVP]** required for v1 launch, **[P2]** after launch.

### 5.1 Identity & Accounts
- **FR-1 [MVP]** Login via email/phone OTP. Social login is [P2].
- **FR-2 [MVP]** RBAC with granular permissions.
- **FR-3 [MVP]** Profile, password reset, mandatory 2FA for admin roles.
- **FR-4 [MVP]** One active session per candidate during an exam.

### 5.2 Question Bank
- **FR-5 [MVP]** CRUD with rich-text editor supporting LaTeX/math, images, code blocks.
- **FR-6 [MVP]** Bulk import/export (CSV/Excel/JSON) with a validation report.
- **FR-7 [MVP]** Versioning (see key rule in section 4).
- **FR-8 [MVP]** Review and approval workflow with comments.
- **FR-9 [P2]** Duplicate detection and usage statistics.
- **FR-10 [MVP]** Multi-language content with links between translated versions (English + Hindi first).

### 5.3 Exam Configuration
- **FR-11 [MVP]** Build manually or by rule (e.g., "10 Easy + 10 Medium + 5 Hard from Topic X").
- **FR-12 [MVP]** Per-exam settings: total and per-section timers; marking scheme (+, −, unattempted); question/option shuffling; section lock; calculator/scratchpad toggle; attempt and retake limits; result release mode (instant, scheduled, manual); **proctoring profile** (section 8).
- **FR-13 [MVP]** Scheduling: start/end window, late-entry cutoff, time zone handling (default Asia/Kolkata).
- **FR-14 [MVP]** Access is **invite-only**. Exams are assigned to batches.
- **FR-15 [MVP]** Admin preview mode.

### 5.4 Candidate Experience
- **FR-16 [MVP]** Dashboard: upcoming, ongoing, completed.
- **FR-17 [MVP]** Instructions page with system check (browser, bandwidth; camera/mic only if the profile needs them) and mandatory acknowledgment.
- **FR-18 [MVP]** Exam UI: question palette with status colors (not visited, not answered, answered, marked, answered+marked); server-synced countdown; Save & Next, Mark for Review, Clear Response; section switching if allowed; zoom, dark mode, accessibility options.
- **FR-19 [MVP]** Autosave every answer (debounced) with resume after disconnect or crash.
- **FR-20 [MVP]** Auto-submit at expiry. **The server is the source of truth.**
- **FR-21 [MVP]** Pre-submit summary and confirmation.
- **FR-53 [MVP]** Low-bandwidth mode: text-first payloads, compressed and lazy-loaded media, resumable autosave.

### 5.5 Proctoring & Integrity (see section 8)
- **FR-22 [MVP]** Tab-switch and fullscreen-exit detection with configurable warnings and violation limit.
- **FR-23 [MVP]** Disable copy/paste, right-click, print during the exam.
- **FR-24 [P2]** Optional webcam snapshots at intervals. **Opt-in per exam; off by default for candidates under 18.**
- **FR-25 [P2]** Optional screen capture. Same restrictions as FR-24.
- **FR-26 [MVP]** IP and device fingerprint logging; multi-login detection.
- **FR-27 [P2]** Flagging engine with risk score; **human review only, no automated penalty for minors.**
- **FR-28 [MVP]** Per-candidate question/option randomization (seeded).
- **FR-29 [MVP]** Admin actions: warn, pause, terminate, invalidate, all audited.

### 5.6 Evaluation & Results
- **FR-30 [MVP]** Auto-scoring per marking scheme, including partial marking for multi-correct.
- **FR-31 [MVP]** Answer-key dispute flow; on key revision, rescore all affected attempts and version the results.
- **FR-32 [MVP]** Result page: score, percentile, rank, section breakdown, correct/incorrect/skipped.
- **FR-33 [MVP]** Solutions and explanations after the release window.
- **FR-34 [P2]** PDF certificates.
- **FR-35 [MVP]** Leaderboards (overall, batch, subject).

### 5.7 Analytics & Reporting
- **FR-36 [MVP]** Candidate analytics: accuracy by topic, time per question, trend, strengths/weaknesses, comparison with topper and average.
- **FR-37 [MVP]** Admin analytics: mean/median/distribution, question difficulty and discrimination index, average time, drop-off points.
- **FR-38 [MVP]** CSV/PDF export of reports.

### 5.8 Notifications
- **FR-39 [MVP]** Email, SMS, in-app: invite, guardian-consent request, exam reminders (24h and 1h), result release, score revision.

### 5.9 Administration
- **FR-40 [MVP]** Audit log of all admin and content changes.
- **FR-41 [P2]** Branding and instruction templates.
- **FR-42 [MVP]** In-exam "report an issue" button.

### 5.10 Batches, Invites, Accommodations
- **FR-50 [MVP]** Batches/institutes as first-class entities; roster import via CSV (name, email/phone, batch, guardian contact for minors).
- **FR-50a [MVP]** Invite lifecycle: single-use links/codes, expiry, revoke, rate limits.
- **FR-49 [MVP]** Accommodations per candidate per exam: extra time, reader/scribe flag, alternate formats.
- **FR-51 [MVP]** Multi-language UI and content (English + Hindi first, extensible).

### 5.11 Privacy & Compliance Operations (section 7)
- **FR-43 [MVP]** Capture age at onboarding. If under 18, block exam access until verifiable guardian consent is on record.
- **FR-44 [MVP]** Consent ledger: versioned notices, timestamp, purpose, withdrawal.
- **FR-45 [MVP]** Guardian portal/link to give, view, withdraw consent.
- **FR-46 [MVP]** Proctoring profile builder; **consent text generated from the profile** so it always matches what is collected.
- **FR-47 [MVP]** Automated retention and deletion of personal data and proctoring media.
- **FR-48 [MVP]** Data-principal requests (access, correction, erasure) with SLA tracking and a grievance contact.
- **FR-52 [MVP]** Incident/breach log with escalation timers.

---

## 6. Non-Functional Requirements

| ID | Area | Requirement |
|---|---|---|
| NFR-1 | Performance | Question load < 500 ms p95; answer save < 300 ms p95 |
| NFR-2 | Scalability | Design for the top scale tier (section 9); load-test at 2× expected peak |
| NFR-3 | Availability | 99.9% overall; 99.95% during scheduled exam windows; change freeze before big exams |
| NFR-4 | Reliability | **No answer loss**: client buffer, retry with idempotency keys, durable server writes |
| NFR-5 | Security | TLS everywhere, encryption at rest, OWASP Top 10, rate limiting, WAF, **question paper encrypted until exam start** |
| NFR-6 | Privacy | DPDP-aligned consent, purpose limitation, retention, PII masking in logs |
| NFR-7 | Accessibility | WCAG 2.1 AA, keyboard navigation, screen-reader support, per-candidate extra time |
| NFR-8 | Compatibility | Latest 2 versions of Chrome, Edge, Firefox, Safari; mobile-first responsive/PWA |
| NFR-9 | Observability | Central logs, metrics, tracing; alerts on error rate, latency, queue lag |
| NFR-10 | DR | RPO ≤ 5 min, RTO ≤ 30 min; automated backups; tested restores |
| NFR-11 | Maintainability | SOLID; ≥ 80% unit-test coverage on core domain; CI/CD; versioned API |
| NFR-12 | Data location | India-region hosting; cross-border transfer off by default |
| NFR-13 | Logging | CERT-In-aligned: retain logs 180 days, India-hosted; 6-hour incident escalation path |

---

## 7. India Compliance Requirements

> Verify with counsel. Dates below reflect the notified DPDP Rules 2025 as of September 2026.

### 7.1 DPDP Act 2023 and Rules 2025
- Rules notified **13 Nov 2025**. Phased enforcement: Consent Manager registration from **14 Nov 2026**; substantive core (notice, consent, children's data, and more) from **14 May 2027**. Build to the final rules from day one.
- **A child is anyone under 18.** Most users of a school-prep platform will be children.
- Before processing a child's data: **verifiable parental consent** using reliable identity and age signals.
- **No tracking, behavioural monitoring, or targeted advertising directed at children.**

### 7.2 Impact on design
- Webcam snapshots, face-presence detection, and behavioural risk scoring may count as behavioural monitoring of minors. Whether an educational exemption applies depends on how the platform is classified. **Legal opinion required before building FR-24, FR-25, FR-27.**
- Until then: proctoring defaults to Off or Browser-lock for under-18 candidates; camera/screen features are explicit per-exam opt-ins with guardian consent; flags are human-reviewed; no automated profiling of minors.
- Store the consent record (who, when, which notice version, purpose) per candidate.
- No advertising or third-party trackers in the candidate experience.

### 7.3 Other items (from general knowledge; verify)
| Area | Plan for |
|---|---|
| Data residency | Host in Indian cloud regions; keep region configurable |
| CERT-In directions (2022) | 6-hour incident reporting; 180-day log retention within India |
| Breach handling | Notify the Data Protection Board and affected users; maintain an incident runbook |
| Accessibility | RPwD Act 2016 and MoSJE guidance on scribes and compensatory time |
| Data-principal rights | Access, correction, erasure, grievance redressal, named grievance officer |
| Retention | Purpose-based retention with automated deletion (e.g., proctoring media deleted N days after result finalization) |
| Language | Notices and UI in English + Hindi + major regional languages |

---

## 8. Configurable Proctoring

Proctoring is a **policy profile** attached to each exam. Adding a profile must not require changing the runtime (OCP).

| Profile | Controls | Intended use |
|---|---|---|
| `OFF` | Nothing | Practice, chapter tests |
| `BROWSER_LOCK` | Fullscreen, tab-switch limits, copy/paste block, event log | **Default for minors** |
| `BROWSER_CAMERA` | Above + periodic snapshots with consent | High-stakes mocks, adult candidates |
| `FULL` | Above + screen capture + human review queue | Adult-only, high-stakes |

**Profile schema (illustrative):**
```json
{
  "id": "BROWSER_LOCK",
  "fullscreenRequired": true,
  "tabSwitchWarnLimit": 3,
  "tabSwitchTerminateLimit": 5,
  "blockClipboard": true,
  "cameraSnapshots": { "enabled": false, "intervalSec": null },
  "screenCapture": { "enabled": false },
  "minAgeForCamera": 18,
  "mediaRetentionDays": 0,
  "requiresGuardianConsent": false
}
```

Rules:
- `IProctorPolicy` resolves the effective policy per candidate (age, accommodations, exam profile). The runtime never checks profile names directly.
- A candidate whose accommodations conflict with a policy (e.g., a screen reader vs. fullscreen lock) gets a documented override.

---

## 9. Scale Tiers

Launch on tier 1; keep interfaces compatible with tier 3.

| Tier | Concurrent test-takers | Approach |
|---|---|---|
| 1. Launch | up to 10k | Modular monolith, single region, Redis + Postgres primary with replicas |
| 2. Growth | 10k to 100k | Extract Exam Runtime as its own service; partition by exam/attempt ID; read replicas; queue-based grading |
| 3. Large | 100k to 1M+ | Sharded stores, multi-AZ, optional multi-region active-passive, staggered slots, CDN-delivered encrypted paper |

Required mechanisms (design in from the start):
- **Encrypted paper pre-download:** the client fetches the encrypted paper before start; the key is released at start time.
- **Write-behind answer storage:** answers land in Redis/log first, flushed durably in batches, with idempotency keys.
- **Staggered entry windows** configurable per exam.
- **Pre-warmed autoscaling** ahead of scheduled exams.
- **Idempotent, queue-based submit** for last-minute surges.
- **Low-bandwidth mode** (FR-53).

---

## 10. Core Data Model (simplified)

- **User** (id, role, DOB/age band, profile, status)
- **GuardianLink** (candidate_id, guardian_id, verified_at, method)
- **ConsentRecord** (subject_id, purpose, notice_version, granted_at, withdrawn_at, given_by)
- **Batch**, **BatchMember**, **Invite** (code, batch_id/exam_id, expires_at, used_at, revoked_at)
- **Subject / Topic** (tree)
- **Question** (id, type, topic_id, difficulty, status) → **QuestionVersion** (content, options, key, explanation, marks, language, translation_group_id)
- **Exam** (id, series_id, config, proctoring_profile_id, schedule, status) → **ExamSection** → **ExamQuestion** (question_version_id, order, marks)
- **Enrollment** (user_id, exam_id, source_invite_id)
- **Accommodation** (user_id, exam_id, type, extra_time_sec, notes)
- **Attempt** (id, user_id, exam_id, started_at, end_time, submitted_at, status, seed)
- **AttemptAnswer** (attempt_id, exam_question_id, response, time_spent, state, updated_at, idempotency_key)
- **ProctorEvent** (attempt_id, type, timestamp, payload)
- **Result** (attempt_id, score, section_scores, rank, percentile, version)
- **Dispute** (attempt_id, question_id, reason, status)
- **DataRequest** (subject_id, type, status, due_at)
- **Incident**, **AuditLog**, **Notification**

---

## 11. Architecture (suggestion; confirm via ADR)

**Style:** modular monolith first, with strict module boundaries so Exam Runtime, Proctoring, and Analytics can be extracted later.

**Modules (one responsibility each):**
1. Identity & Access
2. Consent & Privacy
3. Question Bank
4. Exam Authoring
5. Batches & Invites
6. Exam Runtime (attempt lifecycle, timer, autosave)
7. Proctoring
8. Evaluation & Ranking
9. Analytics & Reporting
10. Notifications
11. Admin & Audit

Modules communicate through interfaces and domain events, never by reaching into each other's tables.

**Suggested stack:**
- Frontend: React/Next.js (PWA), state machine for the exam UI
- Backend: NestJS (Node) or FastAPI/Django (Python); REST + WebSocket
- Data: PostgreSQL, Redis (timers, sessions, leaderboards), object storage (media, snapshots)
- Queue: Kafka or RabbitMQ (grading, ranking, notifications)
- Analytics: OpenSearch or ClickHouse
- Infra: containers on Kubernetes in an India region, CDN, autoscaling, blue/green deploys

**Key interfaces (SOLID in practice):**

| Interface | Responsibility |
|---|---|
| `QuestionTypeHandler` | Grade a response for one question type. New types plug in here (OCP) |
| `IGradingStrategy` | `grade(response, key, scheme) -> Score`; all implementations honor the same contract (LSP) |
| `IAnswerStore` | Persist and read attempt answers |
| `AttemptRepository` | Load/save attempts |
| `Clock` | Time source, injectable for tests |
| `IProctorPolicy` | Resolve effective proctoring rules per candidate |
| `IProctorSink` | Receive proctor events |
| `IConsentService` | Check/record consent; runtime asks "may this candidate proceed?" |
| `EnrollmentPolicy` | Decide who may enroll (invite-only now; paid later without touching runtime) |
| `NotificationChannel` | Email/SMS/in-app senders |
| `PaymentProvider` | Reserved for later; **do not implement in v1** |

**Typed domain errors (examples):** `AttemptExpiredError`, `DuplicateSessionError`, `ConsentRequiredError`, `InviteExpiredError`, `PolicyViolationError`. Each maps to a clean API error code. No swallowed exceptions.

---

## 12. Attempt Lifecycle & Timer

```
Invited → Enrolled → [Consent check] → System Check → Instructions
  → Start (server issues attempt, seed, end_time; releases paper key)
  → In Progress (autosave, heartbeat, proctor events)
  → Submit | Auto-submit | Terminated
  → Grading (async) → Result Published → Review & Dispute window → Final Result
```

**Timer rules:**
- The server stores `end_time` (including accommodation extra time). The client only displays it.
- The client resyncs via heartbeat; requests after `end_time` (plus a small documented grace) are rejected with `AttemptExpiredError`.
- Answer saves are idempotent (`idempotency_key`); replays are safe.
- Comment the *why* of the grace period and clock-skew handling in code.

---

## 13. API Outline (v1)

```
POST /auth/login | /auth/otp/verify
POST /invites/{code}/accept
GET  /consent/status            POST /consent            DELETE /consent/{id}
POST /guardians/consent         (guardian flow)
GET  /exams?status=upcoming
POST /exams/{id}/attempts                 -> start (checks consent + policy)
GET  /attempts/{id}/paper                 -> encrypted paper (seeded shuffle)
PUT  /attempts/{id}/answers/{qid}         -> autosave (idempotent)
POST /attempts/{id}/heartbeat
POST /attempts/{id}/events                -> proctor events
POST /attempts/{id}/submit
GET  /attempts/{id}/result
POST /attempts/{id}/disputes
POST /me/data-requests                    -> access/correction/erasure
Admin: /questions, /exams, /batches, /invites, /reports, /proctor/sessions,
       /accommodations, /incidents
```

All endpoints are versioned (`/v1`), documented (OpenAPI), rate-limited, and return structured errors.

---

## 14. Testing Strategy

- **Unit:** grading strategies (every question type, negative and partial marking), timer/`Clock` logic, rule-based paper generation, rank/percentile math, consent gating, proctor policy resolution.
- **Integration:** full attempt lifecycle including network drop and resume, duplicate-session handling, idempotent autosave, dispute-triggered rescoring.
- **Compliance tests:** minor without guardian consent cannot start an exam; camera features refused for under-18 unless policy and consent allow; retention job deletes expired media; erasure request completes within SLA.
- **Load:** start-time spike, steady-state autosave, last-minute submit surge; run at 2× target.
- **Security:** penetration test, paper-leak scenarios, session hijack, invite-code brute force.
- **Accessibility:** automated WCAG checks plus manual screen-reader pass.
- **UAT:** pilot batch of real candidates.

---

## 15. Delivery Milestones & Acceptance Criteria

Roughly 7 to 8 months for 6 to 8 engineers plus QA and design. M6 and M7 can overlap.

| # | Milestone | Duration | Acceptance criteria |
|---|---|---|---|
| M0 | Discovery & legal | 3 wks | ADR for stack; legal opinion on minors/proctoring recorded; DPDP data map; consent copy drafted |
| M1 | Foundation | 3 wks | Auth + OTP, RBAC, CI/CD, India-region infra as code, consent ledger, audit log |
| M2 | Content | 3 wks | Question CRUD with LaTeX, bulk import with validation report, versioning, review workflow, EN + HI content |
| M3 | Authoring & enrollment | 3 wks | Exam builder (manual + rule-based), scheduling, batches, CSV roster, invite lifecycle, guardian consent flow |
| M4 | Exam runtime | 5 wks | Encrypted paper, seeded shuffle, server-timed attempts, idempotent autosave, palette UI, auto-submit, resume, low-bandwidth mode |
| M5 | Results | 3 wks | Scoring, rank, percentile, solutions, disputes with rescoring and result versioning |
| M6 | Proctoring profiles | 3 wks | `BROWSER_LOCK` fully working; `BROWSER_CAMERA`/`FULL` behind profile config, gated by age and consent |
| M7 | Analytics & privacy ops | 3 wks | Candidate/admin dashboards, retention jobs, data-request workflow, incident log |
| M8 | Hardening | 4 wks | Load test at 2× target passes NFR-1/NFR-4; security test clean; accessibility audit passed; pilot exam completed |
| M9 | Launch | 1 wk | Go-live, monitoring and alerting, hypercare rota |

**Per-milestone checklist for Claude Code:**
1. Create branch `SP/feature/<milestone-slug>` from main/development.
2. Implement against the listed FR/NFR IDs.
3. Write tests first for grading, timer, consent, and policy logic.
4. Add docstrings and *why*-comments as per section 1.3.
5. Commit without co-author trailers.
6. Open a PR listing requirement IDs covered and any assumptions made.

---

## 16. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Traffic spike at exam start | Pre-scaling, staggered entry, CDN-cached encrypted paper, queue-based submit |
| Connection loss mid-exam | Local answer buffer, auto-resume, documented grace policy |
| Question leak | Encrypted paper, key released at start, watermarking, access logging |
| Cheating | Layered proctoring profile + randomization + post-exam analysis |
| Wrong answer key | Dispute flow with bulk rescoring and result versioning |
| Minors' data non-compliance | Guardian consent gate, no behavioural monitoring by default, legal review, retention automation |
| Camera use complaints | Explicit consent, opt-in per exam, retention limits, non-camera alternative |
| Low bandwidth users | Low-bandwidth mode, resumable autosave, mobile-first UI |

---

## 17. Open Questions & Assumptions

**Open (please answer before M4):**
1. Target peak concurrency at launch and in year 2? (Decides when Exam Runtime becomes its own service.)
2. Share of candidates under 18? If most, guardian consent is a launch blocker.
3. Which regional languages beyond English + Hindi, and in what order?
4. Preferred cloud provider and India regions?

**Working assumptions (change if wrong):**
- Modular monolith at launch; extract Exam Runtime at tier 2.
- Social login, certificates, duplicate detection, and camera/screen proctoring are P2.
- Payments are excluded from v1; `EnrollmentPolicy` keeps the door open.
- Default time zone Asia/Kolkata; timestamps stored in UTC.

---

## 18. Definition of Done (any task)

- [ ] Branch created from main/development with the `SP/` prefix before any change
- [ ] Requirement IDs referenced in PR description
- [ ] SOLID respected; no new dependency on a concrete class where an interface exists
- [ ] No silent failures; specific exceptions caught, logged with context
- [ ] Public API documented with docstrings; complex logic has *why*-comments
- [ ] Unit and integration tests added and passing
- [ ] Compliance-sensitive changes (consent, proctoring, retention) have explicit tests
- [ ] No co-author trailer in any commit
