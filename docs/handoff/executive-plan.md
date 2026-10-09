> Copied from the original machine's Claude plan file for the handoff. See PROJECT-HANDOFF.md for the current status.

# Plan: Stabilize M1/M3 of the Online Exam Platform

## Context

`exam-platform-requirements.md` defines milestones M0–M9. The README says M1 (Identity/Consent/Audit) and M3 (Exam authoring, Batches, Invites, Guardian) are complete. A read-only audit (8 area agents plus a completeness critic, every FR/NFR ID checked) found that this claim is wrong:

- **M1/M3 code is partial and has about 15 confirmed high-severity bugs.** The critic re-opened every one and refuted none:
  - OTP brute-force limit never persists.
  - A used OTP can be replayed.
  - Admin 2FA can be bypassed through OTP-only login.
  - Superseded sessions stay valid, because `sid` is never checked.
  - Exam/Batch/Invite/Guardian endpoints have no RBAC, and the creator id comes from the request body.
  - The consent endpoints have an IDOR.
  - The Invite/Batch/Guardian repositories never load child collections, so invites can never be accepted, batches never activate, and link revokes silently no-op.
  - Guardian verify throws `NotImplementedException`.
  - The UI calls about 10 API routes that don't exist.
  - The exam builder always gets a 400.
- **M2 (Question Bank) and M4–M7 (runtime, results, proctoring, analytics/privacy ops, notifications) are entirely missing.**
- Process drift:
  - M3 uses MediatR, which the ADR rejected.
  - M3 uses `DateTime.UtcNow` instead of the injected `Clock`.
  - Architecture tests only cover Identity/Consent/Admin.
  - The README and commits mislabel FR IDs.

**User decisions**

1. **Scope:** stabilize M1/M3 first. Fix every confirmed bug and finish the partial M1/M3 requirements. Later milestones are only listed in the roadmap here.
2. **Delivery:** one `SP/`-prefixed branch per phase, pushed, with a PR per phase that lists FR IDs and assumptions. PRs are stacked: each branches from the previous one.
3. **Guardian verification:** an OTP is sent to the guardian contact.

**Commit rule:** requirements §1.1 forbids co-author and AI-attribution trailers, and the ADR records the same rule. It is the user's project rule, so no commit gets a `Co-Authored-By` line.

Full per-commit designs (files, details, tests, risks) live in `docs/handoff/design/pr{1..4}.md`, with the cross-review in `docs/handoff/design/review.json`. This file is the executive plan. Where the cross-review adjustments below disagree with a design doc, the adjustments win.

## Step 0: Baseline
From `main`, run `dotnet build ExamPlatform.slnx` and `dotnet test ExamPlatform.slnx` (Docker is required for the integration tests). Then run `npm run lint`, `npm run build` and `npm test -- --watch=false` in `apps/web`. Record any failures that already exist, so they aren't blamed on this work.

## PR1 `SP/bugfix/identity-security` (FR-1, FR-3, FR-4, FR-43, NFR-5, NFR-6), from main
1. Test harness: `TestSessions.SignInAsAsync(role, email?, dob?, password?, sessionLifetime?)` mints real sessions and is the **only** test-auth helper for PR1–PR4. ApiFactory becomes non-sealed, with `AdditionalConfiguration`.
2. The global rate limit becomes configurable, and a 429 returns ProblemDetails.
3. OTP `Verify` returns an outcome, so the failed attempt is saved before the error is thrown. Consumed codes are rejected (no replay). OtpChallenge gets an xmin token, plus a shared `SharedKernel.Domain/Exceptions/ConcurrencyConflictError` (409) and a `PostgresConstraintViolations` helper.
4. Issuing a new challenge supersedes the outstanding ones.
5. `LoginEligibilityPolicy`: account status is checked, and users who need 2FA are refused on the OTP-only path. AuthConsentAuditFlowTests switches to password + TwoFactorStep.
6. OTP requests are indistinguishable for unknown, locked and 2FA accounts: a persisted decoy is issued and nothing is sent.
7. Typed 400s for the OTP channel, contact details and a channel/contact mismatch.
8. Date of birth is required and must be plausible.
9. Display name validation.
10. `sid` is validated on every request through Identity-owned `SessionValidatingJwtBearerEvents` (PostConfigure EventsType). The JWT expiry is tied to the session.
11. `POST /v1/auth/logout`.
12. Password policy (12–128 characters, and the email local part is not allowed). A reset revokes sessions and older reset tokens. Password-less users get a no-op.
13. LoggingOtpSender is Development-only, masks contact details, and startup fails fast elsewhere (`Identity:OtpDelivery:Provider`).
14. Named Identity rate-limit policies, ForwardedHeaders, HSTS outside Development, OpenAPI only in Development, and **a security-headers middleware** (a review addition).
15. Web:
    - (a) Logout calls the server, and a banner explains why a session ended.
    - (b) Date-of-birth and display-name validators.
    - (c) Staff 2FA guidance, a password length check, and the destination is passed in router state instead of the URL.
16. README docs.
- Explicitly deferred: auditing Identity register, reset and logout (goes to PR2's audit foundation), per-account password lockout, case-insensitive email uniqueness.

## PR2 `SP/bugfix/m3-authz-persistence` (FR-2, FR-40, section 11), stacked on PR1
Follow the pr2.md commits C1–C32, with these review adjustments:
- **Drop C5 (TestUsers).** Use TestSessions.
- C4: the shared helper in `SharedKernel.Application/Security` carries `GetUserId`, `GetSessionId` and `HasPermission`, and a missing claim is a typed 401.
- C6/C7: `SeriesId` becomes `Guid?` (Guid.Empty is rejected), and the web sends null. This fixes the exam-builder 400 now.
- C8 seeder: an idempotent upsert through `RbacCatalog`, using the D3 matrix. Add a **Development-only bootstrap SuperAdmin** (config and user-secrets, hashed) and `GET /v1/admin/roles`.
- C9 `--migrate-and-seed` for environments other than Development. Its startup test uses that path, or removes the OtpDelivery validator.
- Keep Guardian work minimal, because PR3 replaces the model:
  - Keep: MediatR removal, conventions, the RBAC gate, removing the NotImplemented verify route, typed errors, and the Include fix.
  - Skip: the active-link unique index, the Guardian Clock/event refactor, and GuardianAuditTrail.
- C18: reuse PR1's ConcurrencyConflictError.
- C22: invite codes get only a minimal CSPRNG swap. Name the error `InvalidInviteExpiryError`.
- C25: inject IRequestContext into AssignRoleHandler (CorrelationId). Record the post-commit audit trade-off in ADR 0002.
- C30: the `PageRequest` type (default 50, max 200) and the SharedKernel.UnitTests project are created here.

## PR3 `SP/feature/guardian-consent-flow` (FR-43, FR-44, FR-45), stacked on PR2
Follow the pr3.md commits C1–C23, with these adjustments:
- C5 deletes PR2's Guardian leftovers (tests, FakeClock reuse).
- Staff tokens come from TestSessions. Minors and guardians go through a `RegisterViaApiAsync` helper added to TestSessions.
- Guardian-owned rate-limit policies (`Guardian:RateLimits`).
- The guardian code sender follows PR1's Provider + environment-validator pattern.
- guardian-register navigates with router state, not a query parameter.
- Guardian endpoints map Consent DTOs to Guardian DTOs.
- Use `HasPermission`, `IRequestContext.CorrelationId` and ConcurrencyConflictError.
- Edit RbacCatalog.
- Return the date of birth read-only on the profile, and fix the UserStatus doc.
- Guardian contacts from the roster are **deferred** in both PR3 and PR4, and that is written down.

## PR4 `SP/feature/m3-api-completion` (FR-11 manual, FR-12, FR-13, FR-14, FR-50, FR-50a, FR-2 UI), stacked on PR3
Follow the pr4.md commits C2–C34, with these adjustments:
- **Drop C1.** Add only `PagedResult<T>`.
- C4 is web-only, because the backend part moved to PR2.
- Integration events implement `IDomainEvent` directly.
- Extend PR2's `*AuditTrail` classes instead of adding parallel handlers, and assert one row per event.
- Build on PR2's invite errors, events and xmin.
- Evolve PR2's AddMember validation. Explicitly drop and recreate `IX_BatchMembers_BatchId_Email`.
- `batch.read.all` goes through RbacCatalog.
- Time-travel tests sign in before advancing the clock, with a longer session.
- `Invite:RateLimits:Accept`.
- Add `DELETE /v1/batches/{id}/members/{memberId}`, the Guardian nav item, ExamSection soft-delete consistency plus a unique `(SectionId, Order)` index, and README and .env accuracy fixes.
- The ADR becomes **0003**.

## Delivery rules
- Every PR gets its own branch, created from the previous PR's branch.
- Every commit is small, cites FR/NFR IDs, and carries **no trailer**.
- Each branch is pushed, and one PR is opened per phase against `main`. The PR body lists FR IDs, assumptions and deferrals. PR2–PR4 note that they are stacked.
- Once a PR is open, bind it with ccd_pr and check CI.

## Verification (every PR)
- `cd apps/api && dotnet build ExamPlatform.slnx` (no warnings), then `dotnet test ExamPlatform.slnx` (Docker must be running).
- `dotnet ef migrations has-pending-model-changes` for each changed module, which must report none.
- `cd apps/web && npm run lint && npm run build && npm test -- --watch=false`
- Manual Development smoke steps are listed in each prN.md verification section.

## Roadmap (later milestones, not in this plan)
M2 Question Bank (FR-5..10) → M4 Exam Runtime (FR-16..27, NFR-1/2) → M5 Evaluation & Results (FR-28..34) → M6 Proctoring (FR-26, FR-46..49) → M7 Analytics, Notifications (FR-39) and privacy ops (FR-47/48/52, FR-51 i18n). India-region IaC is out of scope because it is blocked on the cloud-provider decision (section 17 Q4).
