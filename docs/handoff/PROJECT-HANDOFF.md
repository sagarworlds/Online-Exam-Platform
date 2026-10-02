# Online Exam Platform: project handoff and working log

Last updated: 2026-10-02. Written so that a fresh Claude session (another login or another machine) can pick up exactly where the previous one stopped, without the original conversation.

> **How to use this file.** Tell the new Claude session: *"Read `docs/handoff/PROJECT-HANDOFF.md` and continue."* Everything it needs is in this file plus `docs/handoff/design/` (the detailed per-PR designs) and `docs/handoff/executive-plan.md`. Section 10 has the exact steps to resume.

---

## 1. What this project is

- Repository: `https://github.com/sagarworlds/Online-Exam-Platform` (default branch `main`).
- A secure online exam platform for India (DPDP-aligned, guardian consent for minors). The full spec is `exam-platform-requirements.md` in the repo root (FR-1..FR-53, NFR-1..13, milestones M0..M9 in section 15).
- Stack (ADR `docs/adr/0001-stack.md`):
  - Backend: .NET 10 modular monolith, ASP.NET Core Minimal APIs, EF Core 10 + Npgsql (Postgres), one DbContext and schema per module.
  - Frontend: Angular 22 standalone components (`apps/web`).
  - Tests: xUnit, NSubstitute, Testcontainers (Postgres, **needs Docker**), NetArchTest.
  - CI: GitHub Actions (`.github/workflows/ci.yml`).
- Module layout: `apps/api/src/Modules/<Module>/{Domain,Application,Contracts?,Infrastructure,Endpoints}`, `apps/api/src/SharedKernel/*`, host in `apps/api/src/Host/ExamPlatform.Api`.
  - Reference rules (enforced by architecture tests): Domain depends only on SharedKernel.Domain; Application depends on its own Domain and **other modules' Contracts only**; Host references only module Endpoints + SharedKernel.
- Modules today: Identity, Consent, Admin (called "M1" in the docs) and ExamAuthoring, Batch, Invite, Guardian ("M3").

### Project rules that must always be followed (requirements section 1.1 to 1.3)

1. Create a branch before any change, named `SP/<type>/<slug>` (`SP/bugfix/...`, `SP/feature/...`, `SP/chore/...`, `SP/docs/...`).
2. **Never add a co-author trailer to commits**: no `Co-Authored-By:` and no AI-attribution trailer. This is a project rule and it overrides any default attribution instruction a tool may show. Verify with `git log -1 --format=%B`.
3. Small commits, one logical change each. Commit messages and PRs cite FR/NFR IDs (for example `fix(identity): ... (FR-1, NFR-5)`).
4. SOLID, no silent failures, no bare `catch`, XML docstrings on public members plus short "why" comments.
5. Branch + push + one PR per phase against `main`, listing FR IDs and assumptions made.

---

## 2. The user's original request and the decisions they made

First message (verbatim): *"read exam-platform-requirements.md files. Check the incomplete functionalities. Then create a plan and fix one by one"*. They also invoked the `workflow-authoring` skill, so multi-agent workflows are accepted for this work.

Answers to the clarifying questions:

| Question | Decision |
|---|---|
| Scope | **Stabilize M1/M3 first.** Fix every confirmed bug and finish the partially built M1/M3 requirements. M2 and M4..M7 are only a written roadmap for now. |
| Delivery | **One `SP/` branch per phase, pushed, one PR per phase** against `main` (stacked PRs: each branch is created from the previous one). |
| Guardian verification | **OTP to the guardian contact.** The contact comes from registration or the roster. A hashed one-time code is sent; the guardian signs in with a Guardian-role account, confirms the link, then grants consent into the Consent ledger. Staff can override manually and the override is audited. |

Later preferences:

- **Workflow speed (for PR2..PR4):** do not run a reviewer agent after every commit group. Implement the groups in order, run only the affected tests per group, then do the full test run and **one** three-lens review (security, correctness, conventions) at the end.
- Commit whatever is done when asked, but never push or open PRs without it being part of the agreed delivery flow (confirm first when unsure).

---

## 3. What was done, in order

### Step 1: Audit (read-only)

Eight area agents plus a completeness critic audited every FR/NFR against the real code. The critic re-checked every high-severity finding and refuted none. Result: the README's claim that **M1 and M3 are complete is wrong**.

Confirmed high-severity problems:

- **Identity:** the OTP attempt counter is never saved (brute force possible); a used OTP can be replayed; admin 2FA can be bypassed with a Login-purpose OTP; the JWT `sid` claim is never validated, so superseded sessions stay valid; the OTP path ignores user status; `404 user_not_found` allows account enumeration; `Enum.Parse` gives 500s; DateOfBirth defaults to 0001-01-01 (treated as an adult); the logging OTP sender is registered everywhere and logs the code, destination and reset token; no password policy; password reset does not revoke sessions; the seeder returns early when any role exists and seeds only 3 permissions.
- **M3 access control:** ExamAuthoring, Batch, Invite and Guardian endpoints only call `RequireAuthorization()` (no RBAC), and the actor id comes from the request body. The Consent endpoints have an IDOR (any signed-in user can read or change anyone's consent).
- **M3 persistence:** repositories never `Include` child collections, so invites can never be accepted, batches never activate, and guardian link revokes silently do nothing. Not-found and invalid-state errors throw `InvalidOperationException` / `ArgumentException` (HTTP 500). `VerifyGuardianLinkHandler` throws `NotImplementedException`. Invite codes use `System.Random` with no unique index. M3 DbContexts skip the UTC and client-GUID conventions. M3 uses MediatR (ADR rejected it) and `DateTime.UtcNow` instead of the injected `Clock`. There are zero `IDomainEventHandler` implementations, so M3 changes are never audited (FR-40). Architecture tests cover only Identity/Consent/Admin.
- **Web:** about 10 API routes the UI calls do not exist (exams, batches, invites, guardians); the exam builder always gets a 400 (empty `seriesId`); enum drift between UI and API; guardian pages use the user id as the guardian id; no role guards; dev API URL/port mismatch.
- **Missing entirely:** M2 Question Bank, M4 runtime, M5 results, M6 proctoring, M7 analytics/privacy ops, Notifications (FR-39), FR-15 preview, FR-49 accommodations, FR-46/47/48/52, FR-51 i18n.

### Step 2: Planning

- Clarifying questions answered (section 2).
- A design workflow (4 planner agents + a cross-plan reviewer) produced detailed per-PR plans, now in `docs/handoff/design/pr1.md` to `pr4.md`, and the reviewer's adjustments in `docs/handoff/design/review.json`.
- The executive plan is `docs/handoff/executive-plan.md`. **Where the review adjustments disagree with a design doc, the adjustments win** (they are summarised in the executive plan and in section 6 below).

### Step 3: GitHub project board (separate request)

Done with the `gh` CLI. See section 8.

### Step 4: PR1 implementation (identity security)

- Implemented by a long-running Workflow (run `wf_50f2db26-22b`): baseline build/tests, 7 backend commit groups (each implemented and then reviewed/fixed), the web commits in a separate worktree, then integration, full verification, a 3-lens review and a completeness critic.
- The run was interrupted by app restarts several times. **Final review/fixer/critic did not complete.** All code produced so far is committed (section 7).

### Step 5: PR2 implementation (started in parallel)

- Branch `SP/bugfix/m3-authz-persistence` was created from PR1's branch at commit `9c71e65`, in its own git worktree so it does not disturb PR1's folder.
- Workflow run `wf_9a7a0527-73c` (8 groups) implemented 9 commits before being stopped by an app restart (section 7). After the restart, the half-finished permission-policy work was repaired by hand (three small fixes: group-level `.Produces(401)` is invalid, an async test helper disposed its request, guardian flow tests must sign in as staff) and committed.

### Step 6: Housekeeping

- Added an unanchored `.angular/` ignore rule (branch `SP/chore/gitignore-angular-cache`, commit `a33c263`, not pushed). `.angular/` is only the Angular CLI build cache and is never needed in the repo.
- Wrote this handoff file (branch `SP/docs/project-handoff`).

---

## 4. Shared design decisions (D1..D10) every PR follows

- **D1** Remove MediatR from the M3 modules. Use plain sealed handler classes with `HandleAsync(command, ct)` resolved by the endpoints (as Identity does). Delete the central MediatR package version once unused.
- **D2** The actor is always the JWT `sub` claim, never a request body. One shared helper lives in `SharedKernel.Application/Security` (`GetUserId`, `GetSessionId`, `HasPermission`).
- **D3** Permissions (existing naming style): new codes `exam.manage`, `exam.publish`, `batch.manage`, `batch.read`, `invite.manage`, `guardian.link.manage`; existing `admin.audit.read`, `consent.manage`, `identity.role.assign` are kept. Grants: SuperAdmin = all; ExamAdmin = `exam.*`, `batch.*`, `invite.manage`, `guardian.link.manage`; InstituteTeacher = `batch.manage`, `batch.read`, `invite.manage`; Candidate/Guardian = none (self-service routes use auth plus ownership checks). Enforced via the existing `permission:<code>` policy provider. The catalog lives in `Identity.Domain/Rbac/RbacCatalog.cs`; PR4 adds `batch.read.all`.
- **D4** Every not-found / invalid-state / validation failure is a typed `DomainException` subclass (HTTP status + `snake_case` error code), mapped by the Host `DomainExceptionHandler`. No `InvalidOperationException`/`ArgumentException`/`NotImplementedException`/`Enum.Parse` 500s.
- **D5** All time through the injected `Clock`; domain methods take `now`; M3 DbContexts apply `ApplyUtcDateTimeConversion` and `ApplyClientGeneratedGuidKeys`.
- **D6** Cross-module reads only through `*.Contracts` projects; architecture tests cover every module.
- **D7** Cross-module side effects through `IDomainEventHandler<T>` (for example audit via `Admin.Contracts.IAuditLogger`).
- **D8** Repositories `Include` children; every model change ships with a migration; Postgres `xmin` concurrency tokens where single-use or races matter; reuse `SharedKernel.Domain/Exceptions/ConcurrencyConflictError` (409).
- **D9** Unit tests for every changed domain/handler behaviour; integration tests (Testcontainers) proving each high-severity bug is fixed; explicit tests for consent/guardian compliance rules.
- **D10** Web fixes ship in the same PR as the backend they depend on; commits cite FR IDs and carry no trailers.

---

## 5. The four stacked PRs

| PR | Branch | Scope | Requirement IDs | GitHub issue |
|---|---|---|---|---|
| PR1 | `SP/bugfix/identity-security` (from `main`) | OTP persistence/replay/supersede, mandatory admin 2FA, status checks, non-enumerating OTP requests, typed 400s, DOB and display-name validation, `sid` session validation + logout, password policy + reset revokes sessions, dev-only OTP sender with masking and fail-fast, named rate limits + forwarded headers + HSTS + security headers, matching web fixes, README | FR-1, FR-3, FR-4, FR-43 (DOB), NFR-5, NFR-6 | #23 |
| PR2 | `SP/bugfix/m3-authz-persistence` (from PR1) | MediatR removal, UoW ports, arch tests for all modules, shared claims helpers, creator from token, idempotent RBAC seeder + dev bootstrap admin + `GET /v1/admin/roles`, `--migrate-and-seed`, per-route permission policies, typed errors, repository Includes, Clock/UTC conventions, state encapsulation, audit handlers, paging validation, problem+json, docs/ADR 0002 | FR-2, FR-40, NFR-5, NFR-11, section 11 | #24 |
| PR3 | `SP/feature/guardian-consent-flow` (from PR2) | Guardian accounts, guardian OTP link verification, guardian consent into the ledger, consent authorization (IDOR fix), notice purpose/version checks, audited grants, FK, eligibility gate `GET /v1/me/exam-eligibility`, guardian web pages | FR-43, FR-44, FR-45 | #44 |
| PR4 | `SP/feature/m3-api-completion` (from PR3) | List/detail endpoints, FR-12 config, FR-13 scheduling (IANA zones), publish, exam-to-batch assignment, invite accept by code bound to the caller + Enrollment, CSV roster import, member removal, enum drift fixes, role-aware web routing, README corrections | FR-11 (manual), FR-12, FR-13, FR-14, FR-50, FR-50a, FR-2 UI | #45 |

Full per-commit detail (files, tests, risks, migrations, verification) is in `docs/handoff/design/pr1.md` .. `pr4.md`. Open PRs **in order**: PR1 against `main`, PR2 against PR1's branch, and so on (stacked).

---

## 6. Cross-review adjustments that override the design docs

**PR1:** `TestSessions.SignInAsAsync` is the single test-auth helper for all PRs (real sessions, real seeded role and permission claims) and has an optional `sessionLifetime`; never add `perm` claims to `TestJwtTokenBuilder`. `ConcurrencyConflictError` lives in `SharedKernel.Domain/Exceptions`. Add a security-headers middleware. Document how non-Development `WebApplicationFactory` tests override the OTP-delivery validator. Auditing of Identity register/reset/logout events is deferred to PR2's audit foundation.

**PR2:** drop the design's `TestUsers` helper; claims helper must carry `GetUserId`, `GetSessionId` and `HasPermission`; make `SeriesId` `Guid?` (fixes the exam-builder 400 now, not in PR4); seeder through `RbacCatalog` + a Development-only bootstrap SuperAdmin + `GET /v1/admin/roles`; the non-Development startup test must use the `--migrate-and-seed` path; keep Guardian work minimal because PR3 replaces the Guardian model (skip the active-link unique index, the Guardian Clock/event refactor and `GuardianAuditTrail`); reuse PR1's `ConcurrencyConflictError`; minimal CSPRNG invite codes and `InvalidInviteExpiryError`; inject `IRequestContext` into `AssignRoleHandler` for the CorrelationId and record in ADR 0002 that its post-commit audit is an accepted non-atomic trade-off; `PageRequest` (default 50, max 200) and `tests/SharedKernel/ExamPlatform.SharedKernel.UnitTests` are created in PR2.

**PR3:** delete PR2's Guardian leftovers; staff tokens via `TestSessions`, plus a `RegisterViaApiAsync` helper for minors/guardians; Guardian-owned rate-limit policies (`Guardian:RateLimits`); guardian code sender follows PR1's Provider + environment-validator pattern; the guardian register page passes the destination in router state, not the URL; Guardian endpoints map Consent DTOs to their own DTOs; use `HasPermission` and `IRequestContext.CorrelationId`; return the date of birth read-only on the profile; guardian contacts from the CSV roster are deferred in both PR3 and PR4 (written down).

**PR4:** add only `PagedResult<T>` (reuse PR2's `PageRequest`); integration events implement `IDomainEvent` directly with an explicit `OccurredAtUtc`; extend PR2's `*AuditTrail` classes instead of adding parallel handlers (assert exactly one audit row per event); build on PR2's invite errors, events and xmin mapping; evolve PR2's `AddMember` validation and explicitly drop and recreate `IX_BatchMembers_BatchId_Email`; seed `batch.read.all` through `RbacCatalog`; time-travel tests sign in before advancing the fake clock; `Invite:RateLimits:Accept` config; add `DELETE /v1/batches/{id}/members/{memberId}`, a Guardian nav item, ExamSection soft-delete consistency with a unique `(SectionId, Order)` index; ADR number is **0003**.

---

## 7. Current state (2026-10-02)

**Nothing has been pushed to GitHub except the board-automation PR #94, which is merged. All branches below exist only on the machine where the work was done. Push them before switching machines** (see section 10).

### Branches and worktrees (original machine)

| Branch | Worktree folder | State |
|---|---|---|
| `SP/bugfix/identity-security` (PR1) | `D:\Study\Online-Exam-Platform` | 38 commits ahead of `main`, head `fdd2837`, fully committed |
| `SP/bugfix/m3-authz-persistence` (PR2) | `D:\Study\oep-pr2` | based on PR1 `9c71e65`, plus 9 commits, head `c6c5626`, fully committed |
| `SP/chore/gitignore-angular-cache` | `D:\Study\oep-gitignore` | 1 commit `a33c263` |
| `SP/docs/project-handoff` | `D:\Study\oep-gitignore` | this file |
| `SP/chore/project-automation` | `D:\Study\oep-project-automation` | merged as PR #94 |

Untracked and intentionally not committed: `.angular/` (build cache) and `.claude/` (local Claude worktrees and settings).

### PR1 commits (38, oldest first)

```
715e79e test(api): sign integration-test users in with real Identity sessions (FR-4 prep)
229c1f2 chore(api): make the global rate limit configurable and return ProblemDetails on 429 (NFR-5)
caabc0e test(api): prove TestSessions tokens carry the real session, role and perm claims (FR-4 prep)
0353b5a test(api): add TestSessions.RegisterViaApiAsync for tests that need the real sign-up path (FR-1, FR-4 prep)
1b98360 test(api): prove a non-positive global rate limit fails startup (NFR-5)
2c1557f fix(identity): persist failed OTP attempts and reject replayed codes (FR-1, NFR-5)
c7edc0e fix(identity): supersede outstanding OTP challenges when a new one is issued (FR-1)
90d65ae test(identity): pin the exact-expiry boundary shared by Verify and GetOutstandingAsync (FR-1)
82cffdd fix(identity): enforce account status and mandatory admin 2FA when completing OTP login (FR-1, FR-3)
331f7b9 fix(identity): make OTP requests indistinguishable for unknown, locked and staff accounts (FR-1, FR-3, NFR-5)
fe92fbc test(identity): cover the registration-code 2FA rule and the password-login account check (FR-1, FR-3)
2ece832 docs(identity): mention the decoy path in the OtpChallengeIssuer summary (FR-1, NFR-5)
c19f879 fix(identity): return 400s for invalid OTP channels and contact details instead of 500s (FR-1, section 11)
ed81487 fix(identity): require a plausible date of birth at registration (FR-43)
8f1de4e fix(identity): validate display names on registration and profile update (FR-3)
0d9f02e test(identity): cover over-long contacts and blank emails end to end (FR-1, section 11)
4f4b35d feat(identity): validate the session id on every authenticated request (FR-4)
6fa434b feat(identity): add POST /v1/auth/logout that revokes the current session (FR-4)
ed32894 docs(identity): correct the session-secret comment now that sessions are checked per request (FR-4)
bfe0cce feat(identity): enforce a password policy and revoke sessions and older links on reset (FR-3)
3425e8a test(identity): prove one reset link cannot be consumed twice in parallel (FR-3)
ef04910 fix(identity): keep parallel reset requests from revealing that an account exists (FR-1, FR-3, NFR-5)
f703029 fix(api): answer a request body the API cannot read with a 400 instead of a 500 (NFR-5, section 11)
e618fd3 test(identity): prove an access token lives exactly as long as its session (FR-4)
023c4b6 fix(identity): restrict the logging OTP sender to Development, mask PII and fail fast elsewhere (NFR-6, NFR-5)
a31bb01 feat(api): named auth rate limits, forwarded headers and production-only HSTS (NFR-5)
9c71e65 feat(api): security response headers (NFR-5)         <- PR2 was branched from here
fdeb003 fix(api): refuse a forwarded-header trust list given as a single value (NFR-5)
fbaaf0d fix(api): keep HSTS on responses the exception handler writes (NFR-5)
ae25683 refactor(api): drive the API reference mapping and its CSP exemption from one flag (NFR-5)
d94c329 fix(identity): name the offending policy when an auth rate limit is misconfigured (NFR-5)
3199507 test(identity): prove the Development OTP sender is registered and logs only a masked destination (NFR-6)
50b1f18 feat(web): revoke the server session on logout and explain ended sessions (FR-4)
dce2249 fix(web): validate date of birth and display name on registration and profile (FR-43, FR-3)
3f1e9b1 fix(web): guide staff to password + 2FA, enforce password length, keep contact details out of URLs (FR-3, NFR-6)
dc8396b fix(web): count a display name's length after trimming, as the API does (FR-3)
9d617ac docs: document OTP delivery config, session validation, logout and auth rate limits (FR-1, FR-3, FR-4, NFR-5, NFR-6)
fdd2837 fix(api): rate-limit IPv6 clients by their /64 network (NFR-5)
```

**PR1 is not finished:** the final three-lens review (security, correctness, conventions), the fixer and the completeness critic did not complete. Full verification (build, whole test suite, `dotnet ef migrations has-pending-model-changes`, web lint/build/test) was not run as one final pass on the final head.

### PR2 commits (on top of PR1 `9c71e65`, oldest first)

```
50233c5 chore(api): drop MediatR from M3 modules in favour of plain HandleAsync handlers (ADR 0001, FR-2)
713419e refactor(api): M3 unit-of-work ports extend SharedKernel IUnitOfWork
e2257df test(arch): discover modules by assembly name and enforce ADR 0001 rules for every module (NFR-11)
e46b1af feat(sharedkernel): shared claims helpers so every module reads the actor from the JWT (FR-2)
213844c fix(api): take exam/batch/invite creator from the token and let an exam omit its series (FR-2, NFR-5)
4e29743 fix(web): stop sending createdBy/createdByUserId and post a blank exam series as null (FR-2)
a3147d2 fix(identity): idempotent RBAC upsert seeder with the full permission matrix, a development bootstrap admin and GET /v1/admin/roles (FR-2)
142cbb1 feat(host): run idempotent migrate-and-seed outside Development via --migrate-and-seed (FR-2, ADR 0002)
c6c5626 fix(api): enforce per-route permission policies on exam, batch, invite and guardian endpoints (FR-2, NFR-5)
```

**PR2 remaining groups (not started):** G4 typed errors (design C11..C14), G5 persistence (Includes, drop `Invite.CreatedBy`, UTC/GUID conventions, xmin, unique member index; design C15..C19), G6 Clock and state encapsulation, remove the `DomainEvent` base (C20..C24), G7 audit handlers + `IRequestContext` + `PageRequest` + problem+json (C25..C28, C30, C31), G8 docs/ADR 0002 (C32), then full verification, one three-lens review, fixer and critic. A ready-made fix for the Include problem exists on `origin/sp/eager-franklin-94y6x7` (commit `d61ed8f`, with tests) and can be cherry-picked or reimplemented.

**PR3 and PR4:** not started.

**After PR1 gets more commits (review fixes), PR2 must be rebased onto PR1's final head:** `git rebase --onto <pr1-final-head> 9c71e65 SP/bugfix/m3-authz-persistence` (run in the PR2 worktree). Keep PR2's edits to PR1-owned files (Identity, Host, SharedKernel) small to make that clean.

### Baseline facts

- At `main` (commit `ed0f040`) the build passed with 377 warnings (CS1591 missing docs in M3 modules, CS1587, CS9113), all in the four M3 modules; Identity/Host/SharedKernel/tests had 0 warnings, so any warning there after changes is new. All tests passed.
- `dotnet-ef` is installed as a global tool (needed for `dotnet ef migrations ...`).

---

## 8. GitHub project board (Jira-style)

- Project: `https://github.com/users/sagarworlds/projects/2` ("Online Exam Platform", linked to the repo).
- Fields: **Status** (Backlog, Todo, In Progress, In Review, Done), **Priority** (P0 Critical for confirmed bugs, P1 High for MVP, P2 Medium, P3 Low for post-MVP), **Story Points**, **Start date**, **Target date**, **Sprint** (2-week iterations from 2026-10-05, Sprint 1..6).
- Issues `#9`..`#93` (85 issues): one `[Epic]` per milestone M0..M9, with an FR/NFR story or task as a sub-issue each. M1/M3 stories carry the audit gaps and acceptance checklists. 10 repository milestones and 21 labels (`type:`, `priority:`, `area:`, `blocked`, `stabilization`).
  - Stabilization issues: `#23` PR1, `#24` PR2, `#44` PR3, `#45` PR4. Epics: `#9` M0, `#14` M1, `#25` M2, `#33` M3.
  - `#22` (India-region infrastructure as code) is labelled `blocked`: it waits on the cloud-provider decision (requirements section 17, question 4).
- Automation: `.github/workflows/project-automation.yml` (merged in PR #94) adds new issues and PRs to the board and moves cards (issue opened = Backlog, reopened = Todo, closed = Done; draft PR = In Progress; PR ready = In Review; merged = Done; PR events also move the issues the PR closes). It needs the repository secret **`PROJECT_TOKEN`** (classic PAT with `project` + `repo` scopes), which was added; a successful run has not been observed yet.
- `docs/project-management.md` explains the Jira-to-GitHub mapping and the UI-only steps.
- **Still to do by the user in the GitHub UI** (the API cannot do these): create the Board view (column by Status, filter `sprint:@current`), set WIP column limits (In Progress 3, In Review 3), optional Backlog/Roadmap views, and the built-in project workflows. The board currently has more In Progress cards than the limit because epics and the PR1 requirement stories sit there.
- Put `Closes #<issue>` in PR bodies so cards move with the PR (for example PR1 uses `Closes #23`).
- The creation script is idempotent but lived in a temporary scratch folder; it is easy to rewrite from `gh` commands if the board ever needs recreating.

---

## 9. Environment notes and gotchas (learned the hard way)

- **Docker Desktop must be running** for integration tests (Testcontainers). After a machine restart it does not start by itself. If many integration tests fail instantly with `Failed to connect to Docker endpoint`, that is the cause, not the code.
- **Windows, PowerShell + Git Bash.** Node 24 comes through Volta; a multi-line `node -e` script produced no output in Git Bash, use PowerShell instead.
- **`gh` CLI:** install it (`winget install GitHub.cli`) and run `gh auth login --hostname github.com --git-protocol https --web --scopes "project,repo,workflow"`. In PowerShell the PATH of an already-open terminal may be stale: `$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')`.
- **PowerShell function names are case-insensitive**: a helper named `Gh` that calls `gh` recurses forever. Name helpers `Invoke-Gh`.
- **Passing multi-line commit messages:** write the message to a temp file and use `git commit -F <file>`; piping a here-string to `git commit -F -` did not work in PowerShell. Never add a trailer.
- **Line endings:** git prints `LF will be replaced by CRLF` warnings on Windows; they are harmless.
- **Git worktrees:** each parallel branch lives in its own worktree folder (`git worktree add -b <branch> <folder> <base>`). A new worktree has no `bin/obj` and no `node_modules`; the first build takes minutes and the web needs `npm ci`.
- **Workflow resume gotcha:** after editing a workflow script, resuming (`resumeFromRunId`) once re-ran every agent from the baseline and wasted over an hour. Do not edit a script and then expect cached results.
- **Build warnings:** `GenerateDocumentationFile` is on, so missing XML docs on public members produce CS1591 warnings. Keep Identity, Host and SharedKernel warning-free.
- **EF migrations:** every model change needs a migration in the same commit (EF 9+ throws `PendingModelChangesWarning` on `MigrateAsync` otherwise). Command from `apps/api`: `dotnet ef migrations add <Name> --project src/Modules/<Module>/ExamPlatform.Modules.<Module>.Infrastructure --startup-project src/Host/ExamPlatform.Api --context <Module>DbContext --output-dir Migrations`, then `dotnet ef migrations has-pending-model-changes` with the same arguments must report none.
- **Non-Development startup:** after PR1 the host refuses to start outside Development unless `Identity:OtpDelivery:Provider` names a real sender (none exists until FR-39 Notifications). Tests that boot a Production host must remove the `IValidateOptions<OtpDeliveryOptions>` registration and register `CapturingOtpSender`, or use the `--migrate-and-seed` path.
- **Staff login after PR1** is password + one-time code. In Development a bootstrap SuperAdmin is created from config (PR2: `Identity:Bootstrap:*`, password from user-secrets).

---

## 10. How to resume on a new machine or a new Claude login

1. **Push the work first (on the original machine).** Local-only branches are lost if the machine is lost. At minimum push:
   `git push -u origin SP/bugfix/identity-security`, `git push -u origin SP/bugfix/m3-authz-persistence` (from the PR2 worktree), `git push -u origin SP/docs/project-handoff` and `git push -u origin SP/chore/gitignore-angular-cache`.
2. **On the new machine:** install .NET SDK 10 (10.0.100 or later, see `global.json`), Node (see `apps/web/package.json`), Docker Desktop, Git and the `gh` CLI (authenticate as above). Install `dotnet-ef`: `dotnet tool install --global dotnet-ef`.
3. Clone and fetch: `git clone https://github.com/sagarworlds/Online-Exam-Platform.git`, then `git fetch --all`.
4. Check out the branch to continue and set up a PR2 worktree if needed: `git switch SP/bugfix/identity-security`; `git worktree add ../oep-pr2 SP/bugfix/m3-authz-persistence`.
5. Start Docker Desktop, then verify the baseline: from `apps/api` run `dotnet build ExamPlatform.slnx` and `dotnet test ExamPlatform.slnx`; from `apps/web` run `npm ci`, `npm run lint`, `npm run build`, `npm test -- --watch=false`.
6. Tell Claude: *"Read docs/handoff/PROJECT-HANDOFF.md, then continue."* Suggested next actions, in order:
   1. **Finish PR1:** run one final review of `git diff origin/main...HEAD` (security, correctness, conventions lenses), fix what is real, run the full verification, then push and open the PR against `main` with `Closes #23`. Fill the PR description from the FR IDs, the assumptions below and the deferrals below.
   2. **Finish PR2:** rebase onto PR1's final head, implement the remaining groups (section 7), run the full suite and one review, then open PR2 against PR1's branch (`Closes #24`, mention it is stacked).
   3. **PR3 then PR4:** follow `docs/handoff/design/pr3.md` / `pr4.md` with the adjustments in section 6, one branch and PR each.
   4. After all four are merged, plan the roadmap items (M2 Question Bank, M4..M7).
7. Claude's own memory does not travel between logins or machines. This file, the design docs and the GitHub board are the source of truth.

### Assumptions and deferrals to state in the PR descriptions

- Guardian verification uses an OTP to the guardian contact (user decision). The roster-to-guardian-link handoff for CSV imports is deferred.
- Deferred from PR1: auditing Identity register/reset/logout events (goes to PR2's audit foundation), per-account password lockout, case-insensitive email uniqueness, per-destination OTP throttle, TOTP authenticator 2FA, a change-password endpoint, staff provisioning endpoint, httpOnly-cookie tokens.
- Out of scope for this effort: India-region infrastructure as code (blocked on the cloud-provider decision), real SMTP/SMS adapters (FR-39 Notifications), M2 and M4..M9.
- Known accepted trade-off: `AssignRoleHandler` writes its audit entry after the role change commits (non-atomic); recorded in ADR 0002.

---

## 11. Index of files

| File | What it is |
|---|---|
| `docs/handoff/PROJECT-HANDOFF.md` | this file |
| `docs/handoff/executive-plan.md` | the executive plan (steps per PR, delivery rules, verification, roadmap) |
| `docs/handoff/design/pr1.md` .. `pr4.md` | detailed per-commit designs (files, details, tests, migrations, risks, verification) |
| `docs/handoff/design/review.json` | the cross-plan reviewer's conflicts, ordering issues, adjustments and verdict |
| `docs/adr/0001-stack.md` | the stack and module-boundary decision |
| `docs/project-management.md` | how the GitHub project board maps to Jira |
| `exam-platform-requirements.md` | the full requirements and acceptance criteria |

The design docs were generated read-only before implementation and can contain mistakes in details. The real code wins, so verify each claim before relying on it. The raw audit output (about 3,400 lines of JSON) was kept only on the original machine and is not needed: its confirmed findings are in section 3 and in the `covers` lists of the design docs.
