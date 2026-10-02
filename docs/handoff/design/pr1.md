# PR1 SP/bugfix/identity-security
PR1: Identity and auth hardening (FR-1, FR-3, FR-4, FR-43 DOB validation, NFR-5 auth rate limits, NFR-6 OTP/PII logging). This PR fixes every confirmed Identity bug in the audit and needs no RBAC seeding. Themes: (1) OTP verification returns a structured outcome, so a failed attempt is saved before the typed error is thrown, and consumed or superseded codes can't be replayed; (2) OTP requests look the same for unknown, locked and staff accounts (a "decoy" challenge is saved but never sent), and staff must sign in with password plus a TwoFactorStep OTP; (3) the JWT 'sid' is checked against UserSessions on every request, through a JwtBearerEvents subclass that Identity owns and wires in with PostConfigure<JwtBearerOptions>.EventsType, so the Host never references Identity internals; POST /v1/auth/logout revokes the session; (4) typed 400s replace the 500s from Enum.Parse, a missing date of birth, a channel/contact mismatch and bad display names; (5) a password policy applies on reset, and a reset revokes sessions and older reset links; (6) LoggingOtpSender only runs in Development, masks the destination, and startup fails outside Development; (7) named per-IP auth rate-limit policies owned by Identity, ForwardedHeaders, rate-limit rejections as ProblemDetails, HSTS, and OpenAPI only in Development; (8) web: logout calls the server, a banner explains why the session ended, DOB and display-name validators, staff OTP guidance, a password-length hint, and no email or phone in URLs. The integration-test harness moves to real sessions (TestSessions.SignInAsAsync) first, so session validation doesn't break the M3 flow tests.
REQ: FR-1, FR-3, FR-4, FR-43, NFR-5, NFR-6, Section 11 (typed errors, Identity scope), Section 1.2 (no silent failures), Section 1.3 (docstrings + why-comments)

## C1 test(api): sign integration-test users in with real Identity sessions (FR-4 prep)
FILES: D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/TestSessions.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/ApiFactory.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/CapturingOtpSender.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/TestJwtTokenBuilder.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/BatchFlowTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/ExamAuthoringFlowTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/InviteFlowTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/GuardianFlowTests.cs
DETAILS: No production change. This must land before session validation (commit 10); otherwise every TestJwtTokenBuilder token (no 'sid', and roles like 'Admin' that don't exist) gets a 401 and the M3 flow tests break.

TestSessions.cs adds `static Task<SignedInTestUser> SignInAsAsync(this ApiFactory factory, string roleName, string? email = null, DateOnly? dateOfBirth = null, string? password = null)`. In a scope it:
1. Resolves IdentityDbContext and loads the seeded role by name with Include(Permissions); throws if the role is missing.
2. Calls User.Register(unique email `{role}-{guid:N}@tests.local`, null, dob ?? 1990-01-01, ...), then AssignRole and Activate.
3. If a password is given, sets it via SetPasswordHash(IPasswordHasher from DI).
4. Calls user.StartNewSession(SHA-256 of a random secret, now, now+1h, null, null) and saves.
5. Mints the token with the app's own ITokenGenerator from DI.

It returns `record SignedInTestUser(Guid UserId, Guid SessionId, string Email, string AccessToken)`. Because the token is real, it carries the real role and perm claims, which PR2 relies on.

ApiFactory becomes non-sealed. It gets `protected virtual IReadOnlyDictionary<string,string?> AdditionalConfiguration`, merged into the in-memory config, so subclasses can override limits.

CapturingOtpSender gets `bool HasSentTo(string destination)` and `bool TryGetLastCode(string destination, out string code)`.

The four M3 *FlowTests swap `TestJwtTokenBuilder.GenerateAdminToken()` for `(await factory.SignInAsAsync("SuperAdmin")).AccessToken`, and the Guardian token for the "Guardian" role.

TestJwtTokenBuilder's docstring now says it exists only for negative tests (sid-less tokens).
TESTS: All existing Batch/ExamAuthoring/Invite/Guardian flow tests stay green, now signed in with real sessions

## C2 chore(api): make the global rate limit configurable and return ProblemDetails on 429 (NFR-5)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Host/ExamPlatform.Api/Program.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Host/ExamPlatform.Api/RateLimiting/GlobalRateLimitOptions.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Host/ExamPlatform.Api/appsettings.json
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/ApiFactory.cs
DETAILS: Program.cs:63-73 hard-codes 60/min per IP.
- Replace it with `AddOptions<GlobalRateLimitOptions>().Bind(Configuration.GetSection("RateLimiting:Global"))` (PermitLimit=60, WindowSeconds=60 defaults).
- Build the GlobalLimiter inside `services.AddOptions<RateLimiterOptions>().Configure<IOptions<GlobalRateLimitOptions>>(...)`. Why lazy: Program.cs:34-39 already notes that eagerly captured config goes stale under WebApplicationFactory overrides.
- Add `options.OnRejected`: write a ProblemDetails with status 429, title "rate_limited" and a detail, and set Retry-After from `lease.TryGetMetadata(MetadataName.RetryAfter)`. This follows the section 13 rule that every endpoint returns structured errors.
- appsettings.json gets the defaults.
- ApiFactory.AdditionalConfiguration sets RateLimiting:Global:PermitLimit=100000. The new Identity integration tests below send more than 60 requests/min from the single "unknown" TestServer partition and would otherwise flake.
TESTS: Existing suite green; the 429 ProblemDetails shape is asserted in AuthRateLimitTests (commit 14)

## C3 fix(identity): persist failed OTP attempts and reject replayed codes (FR-1, NFR-5)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/OtpVerificationOutcome.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/OtpChallenge.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/Exceptions/OtpAlreadyUsedError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/Exceptions/OtpVerificationOutcomeErrors.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/VerifyOtpHandler.cs
  D:/Study/Online-Exam-Platform/apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Domain/DomainException.cs
  D:/Study/Online-Exam-Platform/apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Domain/Exceptions/ConcurrencyConflictError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/IdentityUnitOfWork.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/IdentityDbContext.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/Migrations/<ts>_OtpChallengeConcurrencyToken.cs (+ .Designer.cs, IdentityDbContextModelSnapshot.cs)
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/OtpChallengeTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/VerifyOtpHandlerTests.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/IdentitySecurityFlowTests.cs (new)
DETAILS: The bug: OtpChallenge.Verify (line 91) does AttemptCount++ (line 103), then throws OtpMismatchError. VerifyOtpHandler.cs:29 therefore never reaches SaveChangesAsync (line 42), and the count is lost.

Fix:
- Verify now returns an `OtpVerificationOutcome` enum {Verified, Mismatch, Expired, AttemptsExceeded, AlreadyUsed}, in this order:
  1. IsConsumed â†’ AlreadyUsed (this closes the replay; IsConsumed at line 114 was never checked).
  2. AttemptCount >= MaxAttempts â†’ AttemptsExceeded.
  3. now > ExpiresAtUtc â†’ Expired.
  4. AttemptCount++; a hash mismatch â†’ Mismatch.
  5. Otherwise set ConsumedAtUtc â†’ Verified.
- Rewrite the docstring: a structured outcome, as section 1.2 allows, so the attempt becomes durable state before the caller reports the error.
- `OtpVerificationOutcomeErrors.ToError(this OtpVerificationOutcome)` maps outcomes to the existing typed errors plus the new OtpAlreadyUsedError ("otp_already_used", 400).
- VerifyOtpHandler: `var outcome = challenge.Verify(hash, clock.UtcNow); if (outcome != Verified) { await unitOfWork.SaveChangesAsync(ct); throw outcome.ToError(); }`, with a why-comment.

Concurrency (D8):
- OtpChallenge gets a Postgres xmin concurrency token: shadow `uint` property configured `.IsRowVersion()`, which Npgsql maps to xmin. Two parallel verifies can then neither double-consume nor under-count attempts.
- IdentityUnitOfWork catches DbUpdateConcurrencyException and throws the new SharedKernel `ConcurrencyConflictError` ("concurrency_conflict", 409). It lives in SharedKernel so the M3 UoWs in PR2 can reuse it.
- Add a protected `DomainException(string message, Exception? inner)` ctor so the cause isn't lost.

Migration: required even if Up is empty, because EF 9+ throws PendingModelChangesWarning on MigrateAsync when the snapshot is stale.
TESTS: Unit OtpChallengeTests.Verify_WithWrongCode_ReturnsMismatchAndIncrementsAttemptCount
  Unit OtpChallengeTests.Verify_AfterMaxWrongAttempts_ReturnsAttemptsExceededEvenForCorrectCode
  Unit OtpChallengeTests.Verify_WhenAlreadyConsumed_ReturnsAlreadyUsed (replay)
  Unit OtpChallengeTests.Verify_AfterExpiry_ReturnsExpired / Verify_WithCorrectCode_ReturnsVerifiedAndConsumes
  Unit VerifyOtpHandlerTests.HandleAsync_WrongCode_SavesAttemptBeforeThrowingOtpMismatchError (unitOfWork.Received(1) + throws)
  Unit VerifyOtpHandlerTests.HandleAsync_ConsumedChallenge_ThrowsOtpAlreadyUsedError_AndMintsNoToken
  Integration IdentitySecurityFlowTests.VerifyOtp_FiveWrongCodes_LocksTheChallengeEvenForTheCorrectCode (5x 400 otp_mismatch, then 429 otp_attempts_exceeded with the real code; DB AttemptCount == 5), which proves the high-severity bug is fixed
  Integration IdentitySecurityFlowTests.VerifyOtp_ReplayingAConsumedCode_IsRejected (2nd verify with the same code gives 400 otp_already_used; the user has exactly 1 UserSession)

## C4 fix(identity): supersede outstanding OTP challenges when a new one is issued (FR-1)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/OtpChallenge.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/OtpVerificationOutcome.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/Exceptions/OtpSupersededError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/Exceptions/OtpVerificationOutcomeErrors.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Ports/IOtpChallengeRepository.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/Repositories/OtpChallengeRepository.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/OtpChallengeIssuer.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/IdentityDbContext.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/Migrations/<ts>_SupersedeOutstandingOtpChallenges.cs (+ Designer, snapshot)
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/OtpChallengeTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/OtpChallengeIssuerTests.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/RequestOtpHandlerTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/IdentitySecurityFlowTests.cs
DETAILS: Domain:
- OtpChallenge gets `DateTime? SupersededAtUtc` and `void Supersede(DateTime nowUtc)`. Supersede is a no-op if the challenge is already consumed or superseded.
- Verify returns the new `Superseded` outcome, checked right after AlreadyUsed. It maps to OtpSupersededError ("otp_superseded", 400, "A newer code has been sent; use the most recent one.").

Persistence:
- IOtpChallengeRepository gets `Task<IReadOnlyList<OtpChallenge>> GetOutstandingAsync(string destination, OtpPurpose purpose, DateTime nowUtc, CancellationToken)`. "Outstanding" means not consumed, not superseded, and ExpiresAtUtc > now.
- EF implementation, plus index IX_OtpChallenges_Destination_Purpose.

Issuer: OtpChallengeIssuer.IssueAsync supersedes every outstanding challenge for (destination, purpose) before adding the new one. Why-comment: the 5-attempt budget now applies only to the latest code. Without this, an attacker holding N parallel challenges gets 5N guesses (audit NFR-5 gap).

Tests: update RequestOtpHandlerTests to stub GetOutstandingAsync with an empty list.
TESTS: Unit OtpChallengeTests.Verify_AfterSupersede_ReturnsSuperseded
  Unit OtpChallengeTests.Supersede_WhenAlreadyConsumed_IsNoOp
  Unit OtpChallengeIssuerTests.IssueAsync_SupersedesOutstandingChallengesForSameDestinationAndPurpose
  Integration IdentitySecurityFlowTests.RequestOtp_Twice_OnlyTheLatestCodeIsAccepted (first challenge id + first code gives 400 otp_superseded; the second works)

## C5 fix(identity): enforce account status and mandatory admin 2FA when completing OTP login (FR-1, FR-3)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/LoginEligibilityPolicy.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Exceptions/TwoFactorLoginRequiredError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Exceptions/OtpPurposeNotAllowedError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/VerifyOtpHandler.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/PasswordLoginHandler.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/User.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/SessionRevocationReason.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityModuleInstaller.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/VerifyOtpHandlerTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/UserTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/AuthConsentAuditFlowTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/IdentitySecurityFlowTests.cs
DETAILS: The bug: VerifyOtpHandler.cs:31-40 issues a full session for any purpose to any user, whatever their status or 2FA requirement. This is how the SuperAdmin logs in, OTP only, at AuthConsentAuditFlowTests.cs:147-155.

LoginEligibilityPolicy (sealed, SRP) has two methods, each returning a `DomainException?` violation instead of throwing:
- `GetAccountViolation(User)`: Suspended or Deactivated gives AccountLockedError.
- `GetOtpPurposeViolation(User, OtpPurpose)`:
  - PasswordReset gives OtpPurposeNotAllowedError (400 "otp_purpose_not_allowed").
  - Login or Registration when `user.RequiresTwoFactor` gives TwoFactorLoginRequiredError (403 "two_factor_login_required", "This account must sign in with its password followed by a one-time code.").
  - TwoFactorStep is always allowed.

Returning the violation lets VerifyOtpHandler save the already-consumed challenge and then throw, so a rejected code can't be retried.

VerifyOtpHandler, after Verified:
1. Load the user.
2. If there is a violation, SaveChanges and then throw.
3. If the purpose is Registration, Activate.
4. Issue the session.

PasswordLoginHandler.cs:36-39 now uses GetAccountViolation instead of the inline check.

Domain:
- `User.RevokeAllSessions(DateTime nowUtc, SessionRevocationReason reason)`.
- `User.Suspend(DateTime nowUtc)`: sets Suspended and revokes all sessions with the new reason AccountSuspended. Needed to arrange tests, and a seam for a future admin endpoint.
- SessionRevocationReason gets AccountSuspended and PasswordReset. It's stored as a string (max 30), so there's no migration.

Register LoginEligibilityPolicy as a singleton.

AuthConsentAuditFlowTests step 6:
- Seed the admin with SetPasswordHash (IPasswordHasher from the scope).
- Log in via POST /v1/auth/login, assert requiresTwoFactor=true plus an otpChallengeId.
- Verify that challenge with GetLastCode(admin email) and use the resulting token.
- Also assert that the old OTP-only path is refused.
TESTS: Unit VerifyOtpHandlerTests.HandleAsync_LoginPurposeForTwoFactorUser_ThrowsTwoFactorLoginRequired_AndPersistsConsumption
  Unit VerifyOtpHandlerTests.HandleAsync_TwoFactorStepForTwoFactorUser_IssuesSession
  Unit VerifyOtpHandlerTests.HandleAsync_SuspendedUser_ThrowsAccountLockedError
  Unit VerifyOtpHandlerTests.HandleAsync_PasswordResetPurpose_ThrowsOtpPurposeNotAllowedError
  Unit VerifyOtpHandlerTests.HandleAsync_RegistrationPurpose_ActivatesUser
  Unit UserTests.Suspend_SetsStatusAndRevokesActiveSessions / RevokeAllSessions_RevokesOnlyActiveSessions
  Integration AuthConsentAuditFlowTests.FullJourney (admin now logs in with password + TwoFactorStep, then reads the audit log and assigns a role)
  Integration IdentitySecurityFlowTests.AdminOtpOnlyLogin_IsRefused (verify of a Login challenge gives 403 two_factor_login_required); updated in the next commit
  Integration IdentitySecurityFlowTests.SuspendedUser_CannotCompleteOtpLogin (403 account_locked)

## C6 fix(identity): make OTP requests indistinguishable for unknown, locked and staff accounts (FR-1, FR-3, NFR-5)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/OtpChallengeIssuer.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/RequestOtpHandler.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/RequestOtpCommand.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/VerifyOtpHandler.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/OtpChallenge.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/RequestOtpHandlerTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/OtpChallengeIssuerTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/IdentitySecurityFlowTests.cs
DETAILS: The bug: RequestOtpHandler.cs:24-27 returns 404 user_not_found, which enumerates accounts. It also issues Login OTPs to suspended and 2FA-required users (line 20/29).

Issuer: OtpChallengeIssuer.IssueDecoyAsync(channel, destination, purpose, ct).
- Supersedes outstanding challenges exactly like the real path.
- Persists an OtpChallenge with UserId=null. Its CodeHash is `codeGenerator.Hash(<64 random hex chars>)`, which no 6-digit input can match.
- Never calls IOtpSender.
- Why-comment: persisting the decoy makes verify behave the same (5 mismatches, then 429). A non-persisted decoy would answer 404 and give the enumeration back one step later.

RequestOtpHandler routes by account state:
- Unknown account, Suspended/Deactivated, or RequiresTwoFactor: decoy. Staff must use password + 2FA.
- PendingVerification: a real Registration-purpose challenge. Verifying it proves control of the destination and activates the account, the same as the original registration OTP.
- Active: Login.

It always returns the challenge id. Remove UserNotFoundError from its docstring.

VerifyOtpHandler: if a Verified challenge has UserId null (unreachable), throw OtpMismatchError rather than UserNotFoundError.

Update the OtpChallenge.UserId docstring: null means pre-account or decoy.
TESTS: Unit RequestOtpHandlerTests.HandleAsync_UnknownDestination_ReturnsChallengeIdWithoutSending (replaces ThrowsUserNotFoundError)
  Unit RequestOtpHandlerTests.HandleAsync_SuspendedUser_IssuesDecoyWithoutSending
  Unit RequestOtpHandlerTests.HandleAsync_TwoFactorUser_IssuesDecoyWithoutSending
  Unit RequestOtpHandlerTests.HandleAsync_PendingUser_IssuesRegistrationPurposeChallenge
  Unit RequestOtpHandlerTests.HandleAsync_ActiveUser_IssuesLoginChallengeAndSendsCode
  Unit OtpChallengeIssuerTests.IssueDecoyAsync_PersistsUnmatchableChallengeAndSendsNothing
  Integration IdentitySecurityFlowTests.RequestOtp_ForUnknownDestination_Returns200WithChallengeIdAndSendsNothing
  Integration IdentitySecurityFlowTests.RequestOtp_ForStaffAccount_SendsNothing_AndPasswordPlusTwoFactorStillWorks (updated AdminOtpOnlyLogin test)
  Integration IdentitySecurityFlowTests.RequestOtp_ForSuspendedAccount_SendsNothing

## C7 fix(identity): return 400s for invalid OTP channels and contact details instead of 500s (FR-1, section 11)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/OtpChannelParser.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityEndpoints.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Exceptions/InvalidOtpChannelError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Exceptions/ContactChannelMismatchError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/Exceptions/ContactRequiredError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/Exceptions/InvalidContactError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/User.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/RegisterCandidateHandler.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/RequestOtpHandler.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/VerifyOtpHandler.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/RegisterCandidateHandlerTests.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/RequestOtpHandlerTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/UserTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/IdentitySecurityFlowTests.cs
DETAILS: Channel parsing:
- OtpChannelParser.Parse(string?) uses Enum.TryParse(ignoreCase). It rejects all-digit strings and values that fail Enum.IsDefined.
- Failure throws InvalidOtpChannelError (400 "invalid_otp_channel", naming Email or Sms).
- It replaces Enum.Parse at IdentityEndpoints.cs:21 and :36.

Contact details:
- User.Register's ArgumentException (User.cs:67) becomes ContactRequiredError (400 "contact_required").
- RegisterCandidateHandler.cs:46 resolves the destination from the chosen channel. If that contact is missing, it throws ContactChannelMismatchError (400 "contact_channel_mismatch") instead of a null destination and a DB not-null 500.
- RequestOtpHandler validates Destination: non-blank and at most 320 chars (matching IdentityDbContext Destination max length). Otherwise InvalidContactError (400 "invalid_contact").

Code: VerifyOtpHandler hashes `command.Code ?? string.Empty`. A null code then counts as an ordinary mismatch attempt instead of throwing a NullReferenceException.
TESTS: Unit UserTests.Register_WithoutEmailOrPhone_ThrowsContactRequiredError
  Unit RegisterCandidateHandlerTests.HandleAsync_EmailChannelWithoutEmail_ThrowsContactChannelMismatchError
  Unit RequestOtpHandlerTests.HandleAsync_BlankDestination_ThrowsInvalidContactError
  Integration IdentitySecurityFlowTests.RequestOtp_WithUnknownChannel_Returns400 ([Theory] "Fax", "5")
  Integration IdentitySecurityFlowTests.Register_WithInvalidChannel_Returns400 / Register_WithEmailChannelButOnlyPhone_Returns400

## C8 fix(identity): require a plausible date of birth at registration (FR-43)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/Requests.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityEndpoints.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/RegisterCandidateCommand.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/RegisterCandidateHandler.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/User.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/Exceptions/InvalidDateOfBirthError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/UserTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/RegisterCandidateHandlerTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/IdentitySecurityFlowTests.cs
DETAILS: The bug: Requests.cs:11 binds a non-nullable DateOnly, so an omitted date becomes 0001-01-01, which counts as Adult and bypasses FR-43 minor detection.

Fix:
- RegisterCandidateRequest.DateOfBirth and RegisterCandidateCommand.DateOfBirth become `DateOnly?`.
- RegisterCandidateHandler throws InvalidDateOfBirthError (400 "invalid_date_of_birth", reason-specific message) when it's null, before any repository call.
- User.Register validates the date against nowUtc. It rejects:
  - default(DateOnly);
  - dates later than DateOnly.FromDateTime(nowUtc).AddDays(1);
  - dates earlier than utcToday.AddYears(-MaximumPlausibleAgeYears), where MaximumPlausibleAgeYears = 120 is a public const.
- Why-comments:
  - The one-day tolerance exists because IST is 5.5h ahead of UTC, so an Indian 'today' can still be 'yesterday' in UTC.
  - Validation lives in the domain because the age band drives guardian-consent gating (section 7.1). An implausible date is a compliance defect, not just bad input.
TESTS: Unit UserTests.Register_WithDefaultDateOfBirth_ThrowsInvalidDateOfBirthError
  Unit UserTests.Register_WithFutureDateOfBirth_ThrowsInvalidDateOfBirthError (utcToday+2)
  Unit UserTests.Register_WithDateOfBirthOneDayAheadOfUtc_IsAccepted
  Unit UserTests.Register_OlderThan120Years_ThrowsInvalidDateOfBirthError
  Unit RegisterCandidateHandlerTests.HandleAsync_MissingDateOfBirth_ThrowsAndAddsNoUser
  Integration IdentitySecurityFlowTests.Register_WithoutDateOfBirth_Returns400InvalidDateOfBirth (compliance: minor detection cannot be bypassed)
  Integration IdentitySecurityFlowTests.Register_WithFutureDateOfBirth_Returns400

## C9 fix(identity): validate display names on registration and profile update (FR-3)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/User.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/Exceptions/InvalidDisplayNameError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/IdentityDbContext.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/UserTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/IdentitySecurityFlowTests.cs
DETAILS: - User gets `public const int MaxDisplayNameLength = 200` and a private static NormalizeDisplayName: trim, then throw InvalidDisplayNameError (400 "invalid_display_name") if the result is empty or longer than 200.
- It's used by both Register and UpdateDisplayName (User.cs:84). This replaces the DB 500 on an empty or >200-char name via PUT /v1/me/profile.
- IdentityDbContext.cs:42 uses User.MaxDisplayNameLength, so the rule and the column can't drift. The value is unchanged, so there's no migration.
- UpdateProfileHandler needs no change beyond the domain call.
TESTS: Unit UserTests.UpdateDisplayName_Blank_ThrowsInvalidDisplayNameError
  Unit UserTests.UpdateDisplayName_Over200Chars_ThrowsInvalidDisplayNameError
  Unit UserTests.UpdateDisplayName_TrimsWhitespace
  Integration IdentitySecurityFlowTests.UpdateProfile_WithInvalidDisplayName_Returns400 ([Theory] "", "   ", 201 chars)

## C10 feat(identity): validate the session id on every authenticated request (FR-4)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Ports/ISessionLookup.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Sessions/SessionSnapshot.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Sessions/SessionValidationResult.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Sessions/SessionValidator.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/Repositories/SessionLookup.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/Authentication/SessionValidatingJwtBearerEvents.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityModuleInstaller.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/ExamPlatform.Modules.Identity.Endpoints.csproj
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/JwtTokenGenerator.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Host/ExamPlatform.Api/Program.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/SessionValidatorTests.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/AuthConsentAuditFlowTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/IdentitySecurityFlowTests.cs
DETAILS: The bug: Program.cs:27 has no OnTokenValidated, so 'sid' (JwtTokenGenerator.cs:30) is never checked. A superseded token stays usable for 12h.

Application:
- Port `ISessionLookup.FindAsync(Guid sessionId, ct)` returns `SessionSnapshot(Guid UserId, DateTime ExpiresAtUtc, DateTime? RevokedAtUtc, SessionRevocationReason? RevokedReason, UserStatus UserStatus)`.
- `SessionValidator.ValidateAsync(Guid userId, Guid sessionId, ct)` (Clock injected) returns `SessionValidationResult` {Valid, Unknown, Superseded, Revoked, Expired, AccountLocked}.
  - A missing session, or a UserId that differs from sub, gives Unknown.
  - RevokedReason SupersededByNewLogin gives Superseded.
  - Any other revocation gives Revoked.
  - ExpiresAtUtc <= now gives Expired.
  - Suspended/Deactivated gives AccountLocked.
  - Each result has a stable error code: session_unknown, session_superseded, session_revoked, session_expired, account_locked.

Infrastructure: SessionLookup is a single AsNoTracking PK query joining UserSessions to Users for Status.

Endpoints: SessionValidatingJwtBearerEvents : JwtBearerEvents.
- TokenValidated: Guid.TryParse the 'sub' and 'sid' claims (raw names, since MapInboundClaims=false). If missing, fail with session_unknown. Otherwise validate; on failure call context.Fail(code), stash the code in HttpContext.Items, and log Information with userId and sessionId only.
- Challenge: when a stashed code exists, HandleResponse() and write 401 with `WWW-Authenticate: Bearer error="invalid_token"` plus a ProblemDetails {title=code, detail=actionable message}. The web reads this title.

Wiring in IdentityModuleInstaller:
- `services.AddScoped<SessionValidatingJwtBearerEvents>(); services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.EventsType = typeof(SessionValidatingJwtBearerEvents));`
- Register ISessionLookup and SessionValidator (scoped).
- Why-comment: Identity owns what 'sid' means. The Host keeps referencing only Identity.Endpoints (ADR 0001 Host rule), and EventsType is resolved per request, so scoped DbContext injection works.
- Add `<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />` to Identity.Endpoints.csproj. The central version already exists.

JwtTokenGenerator:
- Use `expires: session.ExpiresAtUtc, notBefore: session.IssuedAtUtc`.
- Delete TokenLifetime and DateTime.UtcNow at line 47 (the critic's S11-Clock finding). Why: the token must never outlive its session; the 12h lifetime now has one source of truth, LoginSessionIssuer.SessionValidity.

Program.cs: comment only, noting that the Identity module attaches the session check.

Tests: AuthConsentAuditFlowTests step 3 now asserts that the first token gets 401 with title session_superseded on GET /v1/me/profile, and removes the comment saying no live revocation check exists (lines 67-68).
TESTS: Unit SessionValidatorTests.ValidateAsync_ActiveSession_ReturnsValid
  Unit SessionValidatorTests.ValidateAsync_SupersededSession_ReturnsSuperseded
  Unit SessionValidatorTests.ValidateAsync_LoggedOutSession_ReturnsRevoked
  Unit SessionValidatorTests.ValidateAsync_ExpiredSession_ReturnsExpired (FakeClock)
  Unit SessionValidatorTests.ValidateAsync_SessionOfAnotherUser_ReturnsUnknown
  Unit SessionValidatorTests.ValidateAsync_SuspendedUser_ReturnsAccountLocked
  Integration IdentitySecurityFlowTests.SupersededSessionToken_IsRejectedWith401SessionSuperseded (the old token gets 401 while the new token gets 200), which proves FR-4 is fixed
  Integration IdentitySecurityFlowTests.TokenWithoutSessionId_IsRejected (TestJwtTokenBuilder token gets 401)
  Integration IdentitySecurityFlowTests.SuspendingAUser_InvalidatesTheirExistingToken
  Integration AuthConsentAuditFlowTests.FullJourney (live supersede assertion)
  Integration AuthConsentAuditFlowTests.GetProfile_WithoutAToken_ReturnsUnauthorized (unchanged)

## C11 feat(identity): add POST /v1/auth/logout that revokes the current session (FR-4)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/User.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/Exceptions/SessionNotFoundError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/LogoutCommand.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/LogoutHandler.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/ClaimsPrincipalExtensions.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityEndpoints.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityModuleInstaller.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/UserTests.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/LogoutHandlerTests.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/IdentitySecurityFlowTests.cs
DETAILS: - `User.RevokeSession(Guid sessionId, DateTime nowUtc, SessionRevocationReason reason)` calls the internal UserSession.Revoke, which is idempotent. It throws SessionNotFoundError (404 "session_not_found") if the session isn't this user's.
- `LogoutCommand(Guid UserId, Guid SessionId)`. LogoutHandler(IUserRepository, IIdentityUnitOfWork, Clock) loads the user, revokes with LoggedOut, and saves.
- ClaimsPrincipalExtensions gets `GetSessionId()` (reads 'sid'), next to GetUserId. PR2 moves both when it relocates the helper (D2).
- `auth.MapPost("/logout", ...)` with `.RequireAuthorization()` returns 204.
- Register LogoutHandler.
TESTS: Unit UserTests.RevokeSession_OwnActiveSession_MarksLoggedOut
  Unit UserTests.RevokeSession_AlreadyRevoked_IsNoOp
  Unit UserTests.RevokeSession_ForeignSession_ThrowsSessionNotFoundError
  Unit LogoutHandlerTests.HandleAsync_RevokesAndSaves
  Integration IdentitySecurityFlowTests.Logout_RevokesTheSession_AndTheTokenStopsWorking (204; GET /v1/me/profile then gives 401 session_revoked; DB RevokedReason == LoggedOut; a second logout gives 401)
  Integration IdentitySecurityFlowTests.Logout_WithoutToken_Returns401

## C12 feat(identity): enforce a password policy and revoke sessions and older links on reset (FR-3)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Ports/IPasswordPolicy.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/PasswordPolicy.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Exceptions/WeakPasswordError.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/PasswordResetToken.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Ports/IPasswordResetTokenRepository.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/Repositories/PasswordResetTokenRepository.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/IdentityDbContext.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/RequestPasswordResetHandler.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/ResetPasswordHandler.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityModuleInstaller.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/Migrations/<ts>_PasswordResetTokenRevocation.cs (+ Designer, snapshot)
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/PasswordPolicyTests.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/PasswordResetTokenTests.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/ResetPasswordHandlerTests.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/RequestPasswordResetHandlerTests.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/IdentitySecurityFlowTests.cs
DETAILS: The bug: ResetPasswordHandler.cs:35 hashes any password, including empty. It doesn't revoke sessions or other tokens.

Policy (IPasswordPolicy / PasswordPolicy, OCP-swappable):
- `void EnsureAcceptable(string? password, string? email)`: MinLength 12, MaxLength 128 (an upper bound limits PBKDF2 CPU abuse), not whitespace-only, and must not contain the email local part (case-insensitive, when it's 3+ chars).
- Throws WeakPasswordError (400 "weak_password") with an actionable message.
- Why-comment: length over composition, per NIST 800-63B.
- There is no password on candidate registration (OTP-only, FR-1), so the policy applies wherever a password is set: today only reset.

Token domain: PasswordResetToken gets `DateTime? RevokedAtUtc` and `Revoke(nowUtc)`. IsUsable excludes revoked tokens.

Persistence:
- IPasswordResetTokenRepository gets `GetOutstandingForUserAsync(Guid userId, DateTime nowUtc, ct)`.
- xmin row version on PasswordResetTokens (single use under races, D8) and index IX_PasswordResetTokens_UserId.

RequestPasswordResetHandler:
- Silently no-op for users with PasswordHash null, the same response as an unknown email. Why: candidates are OTP-only, and a reset must not create a password login for them.
- Revoke earlier outstanding tokens before issuing the new one.

ResetPasswordHandler:
1. EnsureAcceptable before consuming the token, so a weak password leaves the link usable.
2. Set the hash and consume the token.
3. Revoke the user's other outstanding tokens.
4. user.RevokeAllSessions(now, PasswordReset).
5. Save once.

Register `IPasswordPolicy` â†’ PasswordPolicy (singleton).
TESTS: Unit PasswordPolicyTests.EnsureAcceptable ([Theory] null/empty/11 chars/129 chars/whitespace/contains email local part gives WeakPasswordError; 12+ char passphrase passes)
  Unit PasswordResetTokenTests.Revoked_IsNotUsable
  Unit ResetPasswordHandlerTests.HandleAsync_WeakPassword_ThrowsAndLeavesTokenUnconsumed
  Unit ResetPasswordHandlerTests.HandleAsync_Success_RevokesAllSessionsAndOtherOutstandingTokens
  Unit RequestPasswordResetHandlerTests.HandleAsync_UserWithoutPassword_IssuesAndSendsNothing
  Unit RequestPasswordResetHandlerTests.HandleAsync_RevokesEarlierOutstandingTokens
  Integration IdentitySecurityFlowTests.PasswordReset_WithWeakPassword_Returns400WeakPassword
  Integration IdentitySecurityFlowTests.PasswordReset_Success_EndsExistingSessionsAndInvalidatesOlderLinks. Steps: SignInAsAsync("SuperAdmin", password: ...); request reset twice; the first link gives 400 password_reset_token_invalid; the second succeeds; the old access token gets 401 session_revoked; POST /login with the new password gives requiresTwoFactor=true

## C13 fix(identity): restrict the logging OTP sender to Development, mask PII and fail fast elsewhere (NFR-6, NFR-5)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/OtpDelivery/OtpDeliveryOptions.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/OtpDelivery/OtpDeliveryOptionsValidator.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityModuleInstaller.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Privacy/ContactMasker.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/LoggingOtpSender.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Host/ExamPlatform.Api/appsettings.Development.json
  D:/Study/Online-Exam-Platform/apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/ContactMaskerTests.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/StartupGuardTests.cs (new)
DETAILS: The bug: IdentityModuleInstaller.cs:39 registers LoggingOtpSender unconditionally. LoggingOtpSender.cs:19 logs the code and the raw destination, and via RequestPasswordResetHandler.cs:42 it also logs the reset token.

Options:
- `OtpDeliveryOptions { string? Provider }` with `const string DevelopmentLog = "DevelopmentLog"`, bound from "Identity:OtpDelivery" with `.ValidateOnStart()`.
- `OtpDeliveryOptionsValidator : IValidateOptions<OtpDeliveryOptions>` (injects IHostEnvironment) fails when:
  - Provider is missing or unknown: "No real IOtpSender adapter is configured (Identity:OtpDelivery:Provider); real email/SMS delivery arrives with the Notifications module (FR-39)".
  - Provider == DevelopmentLog and the environment isn't Development.

Sender registration:
- `services.AddScoped<IOtpSender>(sp => ...)` switches on IOptions<OtpDeliveryOptions>.Value.Provider: DevelopmentLog gives ActivatorUtilities.CreateInstance<LoggingOtpSender>(sp); otherwise throw InvalidOperationException (unreachable after start validation).
- Why lazy: config captured eagerly in AddModule can go stale, as the Program.cs:34-39 comment explains.
- The IOtpSender port stays unchanged, and tests keep replacing it with CapturingOtpSender.

Masking:
- `ContactMasker.Mask(OtpChannel, string)` is a static pure function: email gives `j***@example.com`; phone keeps the last 2 digits, e.g. `********10`.
- LoggingOtpSender logs `LogWarning("[DEV ONLY - never enabled outside Development] {Channel} code for {MaskedDestination}: {Code}")`. The code and reset token are still printed, because dev login depends on them, but only in Development and never with the raw email or phone.

Config: appsettings.Development.json gets "Identity": {"OtpDelivery": {"Provider": "DevelopmentLog"}}. appsettings.json deliberately leaves it unset, so non-Development environments fail fast.
TESTS: Unit ContactMaskerTests.Mask_Email_KeepsFirstCharAndDomain / Mask_Phone_KeepsLastTwoDigits / Mask_ShortInputs_NeverRevealWholeValue
  Integration StartupGuardTests.NonDevelopment_WithDevelopmentLogSender_FailsAtStartup (a WebApplicationFactory<Program> subclass with no Postgres container, UseEnvironment("Production"), in-memory Jwt config plus Provider=DevelopmentLog; CreateClient() throws, and the message mentions Identity:OtpDelivery)
  Integration StartupGuardTests.NonDevelopment_WithoutOtpProvider_FailsAtStartup
  Existing ApiFactory suites still boot (Development plus DevelopmentLog from appsettings.Development.json)

## C14 feat(api): named auth rate limits, forwarded headers and production-only HSTS (NFR-5)
FILES: D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/RateLimiting/IdentityRateLimitPolicies.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/RateLimiting/IdentityRateLimitOptions.cs (new)
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityModuleInstaller.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityEndpoints.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Host/ExamPlatform.Api/Program.cs
  D:/Study/Online-Exam-Platform/apps/api/src/Host/ExamPlatform.Api/appsettings.json
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/ApiFactory.cs
  D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/AuthRateLimitTests.cs (new)
DETAILS: The bug: Program.cs:68 has only one global limiter and no ForwardedHeaders.

Identity-owned policies (OCP: the Host doesn't know the auth routes):
- IdentityRateLimitPolicies consts: "identity-otp-request", "identity-otp-verify", "identity-password-login", "identity-password-reset".
- IdentityRateLimitOptions: FixedWindowSettings {PermitLimit, WindowSeconds} per policy, bound from "Identity:RateLimits".
- Defaults per client IP: OtpRequest 20 per 5 min, OtpVerify 30 per 5 min, PasswordLogin 10 per 5 min, PasswordReset 5 per 15 min.
- IdentityModuleInstaller registers them via `services.AddOptions<RateLimiterOptions>().Configure<IOptions<IdentityRateLimitOptions>>((o, l) => o.AddPolicy(name, ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => ...)))`. This composes with the Host's AddRateLimiter and is read lazily.

Endpoints (IdentityEndpoints):
- `.RequireRateLimiting(OtpRequest)` on /otp/request and /register (register sends an OTP: SMS-pumping surface).
- OtpVerify on /otp/verify.
- PasswordLogin on /login.
- PasswordReset on /password-reset/request and /password-reset/reset.

Host Program.cs:
- `Configure<ForwardedHeadersOptions>`: XForwardedFor|XForwardedProto, ForwardLimit=1, KnownProxies / KnownIPNetworks (the .NET 10 name) bound from "ForwardedHeaders:KnownProxies"/"KnownNetworks". Defaults are empty, which trusts loopback only.
- `app.UseForwardedHeaders()` as the first middleware. Why-comment: per-IP partitions behind a proxy otherwise collapse into one bucket, and an untrusted X-Forwarded-For must not let clients pick their partition.
- `if (!app.Environment.IsDevelopment()) app.UseHsts();`
- MapOpenApi/MapScalarApiReference only in Development.

Config and tests:
- appsettings.json gets the Identity:RateLimits defaults and an empty ForwardedHeaders section.
- ApiFactory.AdditionalConfiguration raises all Identity limits to 100000.
- AuthRateLimitTests uses `LowAuthRateLimitApiFactory : ApiFactory` with OtpRequest PermitLimit=2 and PasswordLogin PermitLimit=2.
TESTS: Integration AuthRateLimitTests.OtpRequest_BeyondPolicyLimit_Returns429ProblemDetailsWithRetryAfter (the 3rd request gives 429, title rate_limited, Retry-After header present)
  Integration AuthRateLimitTests.PasswordLogin_BeyondPolicyLimit_Returns429
  Integration AuthRateLimitTests.HealthEndpoint_IsNotAffectedByAuthPolicies
  All other integration suites green with raised limits

## C15 feat(web): revoke the server session on logout and explain ended sessions (FR-4)
FILES: D:/Study/Online-Exam-Platform/apps/web/src/app/auth/auth-api.service.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/app.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/app.spec.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/auth.interceptor.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/auth.interceptor.spec.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/login/login.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/login/login.html
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/login/login.spec.ts
DETAILS: The bug: web logout only cleared localStorage (auth-session.service.ts:36-39, app.ts:22-28).

- AuthApiService gets `logout(): Observable<void>`, which POSTs `${baseUrl}/auth/logout`.
- App.logout() calls `authApi.logout().pipe(finalize(() => { authSession.logout(); void router.navigateByUrl('/login'); })).subscribe({ error: () => undefined })`. Why-comment: local state is always cleared even if the server call fails, e.g. when the session was already revoked.
- authInterceptor: on a 401 for an authenticated request, read the ProblemDetails title (session_superseded, session_revoked, session_expired, account_locked) and navigate to `/login?reason=<title>`. Without a title, keep `/login`.
- Login reads the `reason` query param and shows an info banner (role="status"), for example: session_superseded â†’ "You were signed out because your account signed in on another device."
TESTS: app.spec: 'logout posts to /v1/auth/logout and clears the session' (HttpTestingController expectOne POST; session null afterwards)
  app.spec: 'logout still clears the local session when the server call fails'
  auth.interceptor.spec: 'redirects to /login?reason=session_superseded when the API says the session was superseded' (the existing 401 test updated)
  login.spec: 'shows the signed-in-elsewhere banner for reason=session_superseded'

## C16 fix(web): validate date of birth and display name on registration and profile (FR-43, FR-3)
FILES: D:/Study/Online-Exam-Platform/apps/web/src/app/auth/validators.ts (new)
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/validators.spec.ts (new)
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/register/register.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/register/register.html
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/register/register.spec.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/profile/profile.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/profile/profile.html
DETAILS: validators.ts:
- `dateOfBirthValidator(now: () => Date = () => new Date())` returns errors {required, invalidDate, futureDate, implausibleAge}. It allows one day of tolerance and rejects ages over 120, mirroring User.MaximumPlausibleAgeYears.
- `notBlankValidator`.
- Exported constants MAX_DISPLAY_NAME_LENGTH = 200 and MAX_PLAUSIBLE_AGE_YEARS = 120, with a comment that they must match the backend User constants.

Register:
- dateOfBirth gets [dateOfBirthValidator()], and the date input gets `[max]` = today's ISO date.
- displayName gets [Validators.required, notBlankValidator, Validators.maxLength(200)].
- Inline error messages linked via aria-describedby. Server 400s (invalid_date_of_birth, invalid_display_name, contact_channel_mismatch) still appear through extractErrorMessage.

Profile: the displayName control gets the same validators and messages.
TESTS: validators.spec: future date â†’ futureDate; 121 years ago â†’ implausibleAge; empty â†’ required; today â†’ valid; whitespace name â†’ blank
  register.spec: 'submit is disabled while the date of birth is in the future'
  register.spec: 'shows the server error when the API rejects the date of birth' (flush 400 ProblemDetails)

## C17 fix(web): guide staff to password + 2FA, enforce password length, keep contact details out of URLs (FR-3, NFR-6)
FILES: D:/Study/Online-Exam-Platform/apps/web/src/app/auth/contact-mask.ts (new)
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/contact-mask.spec.ts (new)
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/login/login.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/login/login.html
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/register/register.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/verify-otp/verify-otp.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/verify-otp/verify-otp.html
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/verify-otp/verify-otp.spec.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/password-reset-confirm/password-reset-confirm.ts
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/password-reset-confirm/password-reset-confirm.html
  D:/Study/Online-Exam-Platform/apps/web/src/app/auth/password-reset-confirm/password-reset-confirm.spec.ts
DETAILS: PII out of URLs:
- login.ts:70 and register.ts:48 put the destination in queryParams.
- Pass it as router navigation `state: { destination }` instead. Keep challengeId, purpose and returnUrl as query params.
- VerifyOtp reads the destination from `router.currentNavigation()?.extras.state` (verify the exact Angular 22 API; fall back to `history.state`).
- It renders the destination with maskContact() and copy that doesn't confirm the account exists: "If an account exists for j***@example.com, we've sent a code."
- Losing the destination on a hard reload is acceptable; the form still works.

Staff guidance: the login OTP-mode helper text says "Staff and admin accounts sign in with a password and a one-time code." This matches the backend decoy behavior, which is silent for staff.

Password reset confirm:
- newPassword gets Validators.minLength(12) and maxLength(128), plus a hint. Comment that these match the backend PasswordPolicy.
- The server's weak_password detail is still shown.
TESTS: contact-mask.spec: email/phone masking
  verify-otp.spec: provide the destination via navigation state (not the query) and assert the masked text; the missing-challenge case is unchanged
  login.spec: 'requesting an OTP navigates without putting the destination in the URL' (spy on router.navigate and assert queryParams has no destination)
  password-reset-confirm.spec: 'an 11-character password keeps submit disabled'

## C18 docs: document OTP delivery config, session validation, logout and auth rate limits (FR-1, FR-3, FR-4, NFR-5, NFR-6)
FILES: D:/Study/Online-Exam-Platform/README.md
DETAILS: README.md line 216 dev note:
- OTP codes are printed with a masked destination only in Development (Identity:OtpDelivery:Provider=DevelopmentLog).
- Non-Development startup fails until a real sender exists.

New sections:
- Staff login is password + one-time code; the OTP-only path is refused for staff.
- Session validation: a superseded or logged-out token gets 401 with session_* codes.
- POST /v1/auth/logout.
- Identity:RateLimits and RateLimiting:Global settings, and the ForwardedHeaders:KnownProxies requirement behind a proxy.

Fix the endpoint table's FR labels (lines 397-398 mislabel otp/request and otp/verify as FR-3/FR-4; they are FR-1) and add /v1/auth/logout (FR-4).
TESTS: n/a (docs)

## migrations
- Identity: <timestamp>_OtpChallengeConcurrencyToken. xmin row-version mapping on identity.OtpChallenges. The Up may be empty because xmin is a system column, but the migration is required to update IdentityDbContextModelSnapshot: EF 9+ throws PendingModelChangesWarning on MigrateAsync otherwise. Commit 3.
- Identity: <timestamp>_SupersedeOutstandingOtpChallenges. Adds nullable timestamptz OtpChallenges.SupersededAtUtc and index IX_OtpChallenges_Destination_Purpose. Commit 4.
- Identity: <timestamp>_PasswordResetTokenRevocation. Adds nullable timestamptz PasswordResetTokens.RevokedAtUtc, xmin row version, and index IX_PasswordResetTokens_UserId. Commit 12.
- No migration for new SessionRevocationReason values (AccountSuspended, PasswordReset); they are stored as strings in varchar(30). No migration for the display-name constant, since the length stays 200.
- Command (from apps/api): dotnet ef migrations add <Name> --project src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure --startup-project src/Host/ExamPlatform.Api --context IdentityDbContext --output-dir Migrations. Then run dotnet ef migrations has-pending-model-changes with the same args, which must report none.

## reuse
- D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/OtpChallengeIssuer.cs: the single issue path; extended with supersession plus IssueDecoyAsync
- D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/LoginSessionIssuer.cs: session plus token minting, and SessionValidity as the lifetime source of truth
- D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/User.cs: StartNewSession/UserSession.Revoke (internal, idempotent), RequiresTwoFactor, GetAgeBand
- D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/UserSession.cs: IsActive(now) semantics mirrored by SessionValidator
- D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Exceptions/AccountLockedError.cs, InvalidCredentialsError.cs, and the existing Domain OTP errors (OtpMismatchError, OtpExpiredError, OtpAttemptsExceededError)
- D:/Study/Online-Exam-Platform/apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Domain/DomainException.cs + D:/Study/Online-Exam-Platform/apps/api/src/Host/ExamPlatform.Api/DomainExceptionHandler.cs: every new typed error maps automatically
- D:/Study/Online-Exam-Platform/apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Clock.cs + tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/FakeClock.cs
- D:/Study/Online-Exam-Platform/apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Infrastructure/UtcDateTimeConventions.cs (already applied in IdentityDbContext, so new DateTime? columns are UTC automatically)
- D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/ClaimsPrincipalExtensions.cs: GetUserId, plus the new GetSessionId
- D:/Study/Online-Exam-Platform/apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/PasswordHasher.cs and OtpCodeGenerator.cs (the decoy hash uses the same Hash())
- D:/Study/Online-Exam-Platform/apps/api/tests/ExamPlatform.IntegrationTests/ApiFactory.cs, CapturingOtpSender.cs, and the AuthConsentAuditFlowTests.cs journey style
- D:/Study/Online-Exam-Platform/apps/web/src/app/shared/problem-details.ts extractErrorMessage; auth/auth-session.service.ts; auth/testing/fake-jwt.ts
- New and reusable for PR2+: SharedKernel ConcurrencyConflictError (M3 UoWs), TestSessions.SignInAsAsync (permission-policy tests use real seeded roles and perms), and the per-module rate-limit policy pattern (e.g. invite accept brute force)

## covers
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/VerifyOtpHandler.cs:29 + Domain/OtpChallenge.cs:103: AttemptCount was never saved because OtpMismatchError was thrown before SaveChanges (HIGH), so brute force was possible
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/OtpChallenge.cs:91/114: IsConsumed was never checked, so OTP replay could mint new sessions (HIGH)
- Identity FR-1/NFR-5 gap: older outstanding challenges were not invalidated on re-issue, so each parallel challenge got its own 5 attempts
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/VerifyOtpHandler.cs:40 / RequestOtpHandler.cs:29: mandatory admin 2FA could be bypassed via a Login-purpose OTP (HIGH)
- apps/api/tests/ExamPlatform.IntegrationTests/AuthConsentAuditFlowTests.cs:147-155: SuperAdmin logged in OTP-only; changed to password + TwoFactorStep
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/RequestOtpHandler.cs:20 + VerifyOtpHandler.cs:31: the OTP path ignored user.Status, so Suspended/Deactivated users could sign in (MEDIUM)
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/RequestOtpHandler.cs:24: 404 user_not_found allowed account enumeration; now a uniform 200 with a persisted decoy challenge
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityEndpoints.cs:21,36: Enum.Parse<OtpChannel> returned 500 on bad or numeric input (LOW)
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/RegisterCandidateHandler.cs:46: a channel/contact mismatch gave a null destination and a DB 500 (LOW)
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/User.cs:67: ArgumentException became a typed ContactRequiredError (section 11)
- apps/api/src/Host/ExamPlatform.Api/Program.cs:27: 'sid' was never validated (no OnTokenValidated), so superseded tokens stayed valid for 12h (HIGH, FR-4)
- Identity FR-4 gap: no server-side logout; web logout only cleared localStorage (apps/web/src/app/auth/auth-session.service.ts:36-39, app.ts:22-28)
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/JwtTokenGenerator.cs:47: DateTime.UtcNow bypassed Clock; token expiry is now tied to session expiry (critic S11-Clock, Identity part)
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/Requests.cs:11: an omitted DateOfBirth bound to 0001-01-01 (Adult); future or implausible dates were accepted (MEDIUM, FR-43)
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/ResetPasswordHandler.cs:35: no password policy; reset didn't revoke sessions or other outstanding reset tokens (MEDIUM, FR-3)
- Identity FR-3 gap: a reset for OTP-only candidates silently created a password login; now a no-op
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityModuleInstaller.cs:39 + Infrastructure/LoggingOtpSender.cs:19 + Application/Commands/RequestPasswordResetHandler.cs:42: the dev sender was registered in every environment and logged the code, reset token and raw email/phone (MEDIUM, NFR-6)
- apps/api/src/Host/ExamPlatform.Api/Program.cs:68: only one global per-IP limiter; no OTP/login/reset policies and no ForwardedHeaders (MEDIUM, NFR-5); plus HSTS missing and OpenAPI/Scalar exposed in every environment (critic Program.cs medium)
- Identity FR-3 gap: profile DisplayName was unvalidated, so empty or >200 chars gave a DB 500 (UpdateProfileHandler.cs:18 / User.cs:84)
- apps/web/src/app/auth/login/login.ts:70 + register/register.ts:48: email/phone in the URL query string (LOW, NFR-6)
- apps/web/src/app/auth/register/register.ts:25: DOB only had a 'required' check on the client (FR-43 UI)
- apps/api/tests/ExamPlatform.IntegrationTests/AuthConsentAuditFlowTests.cs:67-68: FR-4 was only checked at the data level; now a live 401 check

## risks
- Session validation adds one indexed PK lookup (AsNoTracking) to every authenticated request. That's fine at tier-1 scale, but it's the first thing to revisit for NFR-1 (Exam Runtime). Any cache must be invalidated on revoke, or FR-4 regresses.
- EF Core 9+/10 throws PendingModelChangesWarning from MigrateAsync when the snapshot lags the model. Every commit that changes the model must include its migration, or the integration suite fails at startup.
- Npgsql xmin mapping: configure a shadow uint property with .IsRowVersion(). Check that the generated migration doesn't try to AddColumn "xmin"; if it does, use the explicit .HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken() form.
- Decoy challenges store the typed destination for non-accounts (non-user PII) until a retention job exists (FR-47/NFR-6). A timing side channel remains, because decoys skip IOtpSender; it becomes negligible once sending is async via Notifications/outbox.
- Brute-force ceiling is now (OTP-request policy limit) Ã— 5 attempts per IP window. There's still no per-destination cap, and a new challenge resets the 5-attempt budget (bounded by the otp-request limit).
- Per-IP defaults can hurt NAT'd schools, where a classroom shares one IP. All limits are configurable (Identity:RateLimits, RateLimiting:Global), but pilots need tuning. A misconfigured ForwardedHeaders:KnownProxies either collapses all clients into one bucket or lets clients spoof X-Forwarded-For.
- Fail-fast is intentional: no non-Development environment can start until a real IOtpSender adapter (Notifications, FR-39) exists and Identity:OtpDelivery:Provider names it. Flag this to whoever owns deployments.
- Staff can't log in without a PasswordHash, and no provisioning endpoint or bootstrap admin exists, so dev databases have no usable admin unless one is seeded by hand with a hash. Suggestion for PR2's seeder upsert: an optional config-driven bootstrap SuperAdmin, Development only.
- TestJwtTokenBuilder tokens (no sid) now get 401. PR2/PR3/PR4 must authenticate tests with TestSessions.SignInAsAsync, so perm claims come from the seeded roles. They must NOT add 'perm' claims to TestJwtTokenBuilder as the critic suggested.
- D2 interaction: PR1 adds GetSessionId next to GetUserId in Identity.Endpoints/ClaimsPrincipalExtensions.cs. When PR2 relocates the shared helper, it must move or keep both consistently, and replace GetUserId's InvalidOperationException/Guid.Parse with a typed error.
- PendingVerification users who request a login OTP now get a Registration-purpose challenge, and verifying it activates the account. PR3's under-18 gate should hook exam eligibility or activation, not login, so minors can still sign in to reach the consent screens.
- The session validator treats only Suspended/Deactivated as locked. If PR3 keeps minors PendingVerification until consent, their sessions stay valid by design.
- The DOB plausibility window (not after UTC today + 1 day, at most 120 years ago) has no minimum age. Product may want one, which is a one-constant change in User and web validators.ts.
- Concurrent parallel logins can still leave two active sessions: there's no DB uniqueness constraint on active sessions, only the aggregate invariant. The window is narrow; see out of scope.
- Web: the masked destination on verify-otp comes from navigation state and disappears on a hard reload (cosmetic only). Confirm the Angular 22 Router API name (currentNavigation() signal vs getCurrentNavigation()).
- Process: section 1.1 and D10 forbid co-author/AI trailers, which conflicts with the harness attribution reminder. Follow the project rule (no trailer) and confirm with the user before committing.

## outOfScope
- RBAC permission codes, IdentitySeeder upsert, and permission policies on the ExamAuthoring/Batch/Invite/Guardian/Consent endpoints (PR2, D3)
- Relocating the shared JWT 'sub' helper for all modules (PR2, D2); PR1 only adds GetSessionId in Identity.Endpoints
- Identity.Contracts (age band / email lookup) and the under-18 guardian-consent gate (PR3, D6); PR1 only makes DOB trustworthy
- Real SMTP/SMS IOtpSender adapters (Notifications module, FR-39); the port stays unchanged
- Per-account password lockout/backoff (PasswordLoginHandler.cs:31, medium, not in the critic-confirmed list): it can be abused as targeted DoS against admins; the named per-IP login policy mitigates for now
- Email/phone normalization and case-insensitive uniqueness (UserRepository.cs:23, low): needs a data migration and a citext/lower() index; follow-up
- Register 409 duplicate_account enumeration: needs a 'we sent you a code' flow for existing accounts
- Per-destination OTP throttle / resend-cooldown endpoint and UI
- Keyed (HMAC) OTP/session hashing instead of unsalted SHA-256 of a 6-digit space
- TOTP/authenticator 2FA option; change-password endpoint for signed-in users; staff/admin provisioning endpoint
- Moving the bearer token from localStorage to an httpOnly cookie; sending X-Device-Fingerprint from the web (FR-26); a SessionSupersededEvent handler / proctor flag (FR-26); binding sessions to exam attempts (M4)
- DB-level guard against concurrent logins creating two active sessions
- UseHttpsRedirection (TLS terminates at the proxy/WAF; HSTS is added)
- Global JsonStringEnumConverter(allowIntegerValues:false) for other modules' enums (M3 enum handling belongs to PR2/PR4)
- The DomainEvent.cs DateTime.UtcNow and M3 Clock adoption (PR2, D5)

## verification
- cd D:/Study/Online-Exam-Platform/apps/api && dotnet build ExamPlatform.slnx -c Release (no warnings-as-errors; docstrings present, because GenerateDocumentationFile is on)
- dotnet test tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests
- dotnet test tests/ExamPlatform.ArchitectureTests (Host still references only Endpoints; Identity.Infrastructure still has no other-module deps)
- dotnet test tests/ExamPlatform.IntegrationTests (Docker running) â€” targeted run: --filter "FullyQualifiedName~IdentitySecurityFlowTests|FullyQualifiedName~AuthConsentAuditFlowTests|FullyQualifiedName~AuthRateLimitTests|FullyQualifiedName~StartupGuardTests"; then the full run to confirm the Batch/ExamAuthoring/Invite/Guardian flows still pass with real sessions
- dotnet test ExamPlatform.slnx (full CI-equivalent run)
- dotnet ef migrations has-pending-model-changes --project src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure --startup-project src/Host/ExamPlatform.Api --context IdentityDbContext (should report no changes); dotnet ef migrations list with the same args shows the 3 new migrations
- cd D:/Study/Online-Exam-Platform/apps/web && npm run lint && npm run build && npm test -- --watch=false
- Manual (Development):
1. Register with a DOB, then confirm the console log shows a masked destination and the '[DEV ONLY]' prefix.
2. Log in via OTP twice, then call GET /v1/me/profile with the first token. Expect 401 with title session_superseded; the web should show the 'signed in on another device' banner.
3. Click Log out, then confirm the old token gets 401 session_revoked.
4. Request an OTP for an unknown email. Expect 200 with an otpChallengeId and no code logged.
- Manual: set ASPNETCORE_ENVIRONMENT=Production with no Identity:OtpDelivery:Provider and run dotnet run. Startup should fail with the OtpDelivery validation message. Also confirm Scalar/OpenAPI aren't mapped in Production.
- git log for the branch: every commit message cites FR/NFR IDs and has no Co-authored-by or other trailer (section 1.1 / D10)

