# PR3 SP/feature/guardian-consent-flow
PR3 (stacked on PR2): "Guardian consent flow: OTP-verified guardian links, guardian consent in the ledger, consent authorization and the FR-43 under-18 gate (FR-43, FR-44, FR-45, FR-40, FR-2, NFR-5, NFR-6)".

Summary for the PR description:
- A guardian is now an Identity user with the Guardian role. Sign-up goes through POST /v1/auth/register with accountType=Guardian. Only Candidate or Guardian can be chosen, and a guardian must be an adult. An existing adult account can add the role via POST /v1/me/roles/guardian.
- GuardianLink is its own aggregate root (candidate_id, guardian_id, verified_at, method; section 10). A link request can come from the candidate (POST /v1/me/guardian-links) or from staff (POST /v1/admin/guardian-links). It issues a salted, HMAC-hashed, 6-digit code that expires and allows a limited number of attempts. The code is sent to the guardian's email or phone through a Guardian-owned sender port. The Development adapter masks the destination and logs the code only when a config flag is on.
- The signed-in guardian submits the code (POST /v1/guardians/me/links/{linkId}/verify) and the link becomes Verified. This replaces the NotImplementedException. Failed attempts are saved before the typed error is thrown, and an xmin concurrency token guards against parallel guesses.
- Staff with guardian.link.manage can verify a link manually (POST /v1/admin/guardian-links/{id}/attest). A reason is required and the action is audited.
- The guardian gives or withdraws consent for the linked minor through POST /v1/guardians/consent and DELETE /v1/guardians/consent/{id}. The ledger records GivenById = the guardian's user id and GivenByKind = Guardian.
- The Consent IDOR is fixed. One ConsentAccessPolicy decides every read, grant and withdraw. The caller must be the subject, a verified guardian of a minor subject, or hold consent.manage. Minors cannot grant consent themselves. Guardians cannot grant for adults.
- RecordConsent checks that the notice's purpose matches the request and that the notice is the current effective version. It rejects a duplicate active grant and supersedes a grant made against an older notice.
- Grants and withdrawals are both audited, atomically. The audit entry is written to a Consent-local audit outbox table in the same commit as the ledger change, then relayed to IAuditLogger with retries.
- The database now enforces the ledger rules: a foreign key from ConsentRecords.NoticeVersionId, a partial unique index on active records, and xmin.
- New FR-43 gate IConsentGate.MayCandidateProceedAsync, plus GET /v1/me/exam-eligibility. Invite acceptance is deliberately not gated.
- New Identity.Contracts (IUserDirectory: age band, roles, display name) and Guardian.Contracts (IGuardianshipQuery) projects. Architecture tests are extended to cover them.
- Web: guardian sign-up, the candidate's "request guardian consent" page, the guardian verify-code page, a guardian dashboard on the /me endpoints, and guardian consent grant/withdraw. The candidate consent page follows the minor rules and shows the notice version and exam eligibility.

Commit messages carry no Co-authored-by or AI trailer (section 1.1 / D10).
REQ: FR-43, FR-44, FR-45, FR-40, FR-2, NFR-5, NFR-6, S11-IConsentService, S10-GuardianLink, S10-ConsentRecord, S13-ConsentAPI, S13-GuardiansConsentAPI, Section-11 typed errors, Section-1.2 no silent failures, Section-1.3 docstrings/why-comments

## C1 feat(identity): add Identity.Contracts with IUserDirectory age-band and role lookup (FR-43)
FILES: apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Contracts/ExamPlatform.Modules.Identity.Contracts.csproj (new; references SharedKernel.Domain only)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Contracts/AgeBand.cs (new)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Contracts/UserSummary.cs (new)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Contracts/IUserDirectory.cs (new)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Contracts/WellKnownRoleNames.cs (new)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/AgeBandCalculator.cs (new)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/User.cs (GetAgeBand delegates to AgeBandCalculator)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/UserDirectory.cs (new)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Ports/IUserRepository.cs (+ListByIdsAsync)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/Repositories/UserRepository.cs
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/ExamPlatform.Modules.Identity.Application.csproj (+ref Identity.Contracts)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityModuleInstaller.cs (AddScoped<IUserDirectory, UserDirectory>)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/IdentitySeeder.cs and Application/Commands/RegisterCandidateHandler.cs (use WellKnownRoleNames.Candidate/Guardian constants)
  apps/api/ExamPlatform.slnx (add project)
  apps/api/tests/ExamPlatform.ArchitectureTests/ContractsLayerTests.cs + ExamPlatform.ArchitectureTests.csproj (add Identity.Contracts row)
  apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/AgeBandCalculatorTests.cs (new)
  apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/UserDirectoryTests.cs (new)
DETAILS: Why: other modules currently have no way to ask whether a user is a minor (audit FR-43 gap: 'Identity has no Contracts project'). ADR 0001 lets Application layers reference only other modules' Contracts. Contracts types: enum AgeBand {Minor, Adult}, a mirror of the Domain enum kept separate like Consent.Contracts.ConsentPurpose. sealed record UserSummary(Guid UserId, string DisplayName, AgeBand AgeBand, IReadOnlyCollection<string> RoleNames) with a bool IsInRole(string) helper. IUserDirectory { Task<UserSummary?> FindAsync(Guid userId, CancellationToken); Task<IReadOnlyDictionary<Guid, UserSummary>> FindManyAsync(IReadOnlyCollection<Guid>, CancellationToken) } returns null or omits unknown ids, and the caller throws its own typed error. WellKnownRoleNames exposes only Candidate and Guardian; a doc comment explains that staff role names are deliberately not part of any self-service contract. AgeBandCalculator.Compute(DateOnly dob, DateTime asOfUtc) moves the existing User.GetAgeBand math (User.cs:91-97) into one pure function so the directory can compute bands without loading the aggregate; add a why-comment on the birthday-boundary rule. UserDirectory (Application) uses IUserRepository plus the injected Clock and maps Domain to Contracts with an explicit switch (same pattern as ConsentService.ToDomain). Role names come from the database, not JWT role claims (see risks: the ClaimTypes.Role inbound mapping). Docstrings on every public member.
TESTS: AgeBandCalculatorTests.DayBefore18thBirthday_IsMinor / On18thBirthday_IsAdult / LeapDayDob_Feb28NonLeapYear_IsStillMinor: proves the section 7.1 'under 18' boundary is exact
  UserDirectoryTests.FindAsync_UnknownUser_ReturnsNull: proves no exception is used as control flow
  UserDirectoryTests.FindAsync_MapsRolesAndAgeBandUsingClock: FakeClock set to the day before and after the 18th birthday
  ContractsLayerTests row for Identity.Contracts: no EF/ASP.NET/other-module dependencies

## C2 feat(identity): expose age band on GET /v1/me/profile (FR-43)
FILES: apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Dtos/UserProfileDto.cs (+string AgeBand)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Queries/GetProfileHandler.cs (inject Clock)
  apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/GetProfileHandlerTests.cs (new or extended)
DETAILS: The web consent page needs to know it is serving a minor so it can hide self-grant (fix for consent.ts:54). The band is recomputed on every request, never stored, because it changes over time (keep the existing User doc comment). AgeBand goes at the end of the positional record so existing JSON consumers keep working. The web model update ships in the web commit of this PR (D10).
TESTS: GetProfileHandlerTests.Minor_ReturnsAgeBandMinor and Adult_ReturnsAgeBandAdult (FakeClock)

## C3 feat(identity): allow self-registration as Guardian (adult only), never staff roles (FR-45, FR-2)
FILES: apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/SelfServiceAccountType.cs (new enum Candidate|Guardian)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/RegisterCandidateCommand.cs (+AccountType, default Candidate)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/RegisterCandidateHandler.cs
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/BecomeGuardianCommand.cs (new)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/BecomeGuardianHandler.cs (new)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Exceptions/GuardianMustBeAdultError.cs (new, 400 guardian_must_be_adult)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Exceptions/InvalidAccountTypeError.cs (new, 400 invalid_account_type)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/Requests.cs (RegisterCandidateRequest + string? AccountType = null)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityEndpoints.cs (register mapping; POST /v1/me/roles/guardian)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityModuleInstaller.cs (register BecomeGuardianHandler)
  apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/RegisterCandidateHandlerTests.cs
  apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/BecomeGuardianHandlerTests.cs (new)
DETAILS: Fixes the missing guardian sign-up path (audit app.routes.ts:84 and IdentitySeeder.cs:42: the Guardian role is never assigned). The endpoint accepts only the closed SelfServiceAccountType enum. Parsing uses Enum.TryParse plus Enum.IsDefined and rejects numeric strings (TryParse would accept '1'); anything else, including 'SuperAdmin', gets InvalidAccountTypeError. The handler maps Candidate to WellKnownRoleNames.Candidate and Guardian to WellKnownRoleNames.Guardian; a why-comment explains that a role name is never taken from the request. Guardian requires AgeBandCalculator(dob, clock.UtcNow) == Adult, otherwise GuardianMustBeAdultError; a why-comment says verifiable parental consent requires an adult. The guardian gets the Guardian role only, not Candidate. BecomeGuardianHandler (POST /v1/me/roles/guardian, auth, userId from sub) lets an existing adult account (e.g. a parent who is also a candidate) add the Guardian role without hitting DuplicateAccountError. It is idempotent, adult only, and audited as 'Identity.RoleAssigned' with actor = self via the same IAuditLogger pattern AssignRoleHandler uses. The Guardian role carries no permissions (D3), so this self-grant cannot escalate privileges. No schema change.
TESTS: RegisterCandidateHandlerTests.AccountTypeGuardian_Adult_AssignsGuardianRoleOnly
  RegisterCandidateHandlerTests.AccountTypeGuardian_Minor_ThrowsGuardianMustBeAdultError
  RegisterCandidateHandlerTests.DefaultAccountType_AssignsCandidateRole (regression)
  BecomeGuardianHandlerTests.Minor_Throws / Adult_AssignsRoleOnce_Idempotent / Audits

## C4 feat(guardian): add Guardian.Contracts guardianship query port (FR-43, FR-45)
FILES: apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Contracts/ExamPlatform.Modules.Guardian.Contracts.csproj (new; SharedKernel.Domain only)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Contracts/IGuardianshipQuery.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Contracts/GuardianshipSummary.cs (new)
  apps/api/ExamPlatform.slnx
  apps/api/tests/ExamPlatform.ArchitectureTests/ContractsLayerTests.cs + csproj (Guardian.Contracts row)
DETAILS: The ports: IGuardianshipQuery { Task<bool> IsVerifiedGuardianAsync(Guid guardianUserId, Guid candidateId, CancellationToken); Task<GuardianshipSummary> GetForCandidateAsync(Guid candidateId, CancellationToken) } and sealed record GuardianshipSummary(Guid CandidateId, IReadOnlyCollection<Guid> VerifiedGuardianUserIds, bool HasPendingRequest). Consent (access policy and gate) and later Invite/Runtime ask 'is X a verified guardian of Y' only through this port (D6). It is implemented and registered by the Guardian module in the handlers commit.
TESTS: ContractsLayerTests row for Guardian.Contracts

## C5 refactor(guardian): remove the unverifiable token-based guardian link flow (FR-45)
FILES: apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/GuardianCommands.cs (delete)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/GuardianHandlers.cs (delete; removes NotImplementedException at :62)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Dtos/GuardianDto.cs (delete)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Ports/IGuardianRepository.cs (delete)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianRepositoryAndUnitOfWork.cs (keep only GuardianUnitOfWork; move to GuardianUnitOfWork.cs)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianEndpoints.cs (remove all five routes and request records)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianModuleInstaller.cs (drop handler registrations)
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/CreateGuardianHandlerTests.cs (delete)
  apps/api/tests/ExamPlatform.IntegrationTests/GuardianFlowTests.cs (delete; replaced by GuardianConsentFlowTests)
DETAILS: The old routes (POST /v1/guardians, POST /{guardianId}/links, POST /links/verify, DELETE /{guardianId}/links/{candidateId}, DELETE /{guardianId}/candidates/{candidateId}) are fundamentally broken. The guardian id is unrelated to the caller, there is no authorization, the token is plaintext with no expiry and is never delivered, and verify throws. Deleting them first keeps each later commit reviewable and every commit buildable. The DbContext and migration stay untouched until the next commit. Nothing in the web app keeps working in between, which is acceptable inside a stacked PR.
TESTS: dotnet build passes; no other test references the deleted types

## C6 feat(guardian): model GuardianLink as an OTP-verified aggregate bound to Identity users, with persistence (FR-45, S10-GuardianLink)
FILES: apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Guardian.cs (rewrite: thin guardian-portal profile, Id == Identity user id)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/GuardianLink.cs (rewrite: AggregateRoot)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/GuardianContact.cs (new value object)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/GuardianContactChannel.cs (new: Email|Sms)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/GuardianVerificationChallenge.cs (new owned value object)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/CodeCheckOutcome.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/GuardianVerificationMethod.cs (new: OtpToGuardianContact|StaffAttestation)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/LinkRequesterKind.cs (new: Candidate|Staff)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/GuardianLinkStatus.cs (keep Pending|Verified|Revoked, add docs)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Events/GuardianLinkRequestedEvent.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Events/GuardianLinkVerifiedEvent.cs (rewrite)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Events/GuardianLinkVerificationLockedEvent.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Events/GuardianLinkRevokedEvent.cs (rewrite)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Events/GuardianEnrolledEvent.cs (new; replaces GuardianCreatedEvent.cs, deleted)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Exceptions/{GuardianLinkNotFoundError,GuardianLinkNotPendingError,GuardianCodeMismatchError,GuardianCodeExpiredError,GuardianCodeAttemptsExceededError,GuardianCodeResendLimitError,InvalidGuardianContactError,AttestationReasonRequiredError,GuardianCannotLinkSelfError}.cs (new); delete InvalidLinkTokenError.cs and GuardianNotFoundError.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianDbContext.cs (remap)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/Migrations/<ts>_GuardianLinkVerification.cs + Designer + GuardianDbContextModelSnapshot.cs (generated)
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/FakeClock.cs (new, copy of the Consent test double)
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/GuardianLinkTests.cs (new)
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/GuardianContactTests.cs (new)
DETAILS: Why a separate aggregate: a link request exists before the guardian has an account, so it cannot be a child of Guardian. The old handler comment admits it needs 'a dedicated link repository'.

GuardianLink (private setters; all time comes from nowUtc parameters, D5):
- Fields: Id, CandidateId, GuardianId (Guid?, set on verify, FK to Guardians.Id), Contact (Channel, Destination; Destination nullable), ContactHint (masked, always kept for UI and audit), Status, RequesterKind, RequestedById, RequestedAtUtc, VerificationMethod?, VerifiedById?, VerifiedAtUtc?, AttestationReason?, RevokedById?, RevokedAtUtc?, Challenge.
- Challenge fields: CodeHash, CodeSalt, IssuedAtUtc, ExpiresAtUtc, FailedAttempts, IssueCount.
- Behaviour:
  - static Request(candidateId, contact, requesterKind, requestedById, hashedCode, nowUtc) raises GuardianLinkRequestedEvent(LinkId, CandidateId, RequestedById, RequesterKind, Channel, OccurredAtUtc). No destination is in the event (NFR-6; audit flagged PII in GuardianCreatedEvent.cs:5).
  - ReissueCode(hashedCode, nowUtc): Pending only. Enforces a 60 s cooldown and at most 5 issues, else GuardianCodeResendLimitError (429). Resets FailedAttempts.
  - CodeCheckOutcome CheckCode(suppliedHash, guardianUserId, nowUtc): does not throw for mismatch, expiry or lockout, so the handler can persist the attempt first. This avoids the Identity bug where AttemptCount is lost because Verify throws before save. Order: not Pending gives NotPending; FailedAttempts >= 5 gives LockedOut; now > ExpiresAtUtc gives Expired; CryptographicOperations.FixedTimeEquals mismatch increments FailedAttempts and returns Mismatch, raising GuardianLinkVerificationLockedEvent when it reaches 5; a match returns Verified. guardianUserId == CandidateId throws GuardianCannotLinkSelfError. On Verified: Status=Verified, GuardianId, Method=OtpToGuardianContact, VerifiedById=guardian; raises GuardianLinkVerifiedEvent.
  - AttestByStaff(guardianUserId, staffUserId, reason, nowUtc): Pending only. Reason must be trimmed and 10-500 chars, else AttestationReasonRequiredError. Method=StaffAttestation.
  - Revoke(byUserId, nowUtc): Pending or Verified; raises GuardianLinkRevokedEvent(LinkId, CandidateId, GuardianId?, RevokedById).
  - Every terminal transition clears Contact.Destination and the challenge hash (data minimisation, NFR-6).
- Why-comments: 24 h validity (guardians are not online instantly; 5 attempts x 5 issues bounds the guess probability at 25/10^6), the cooldown, and the attempt persistence.

GuardianContact.Create(channel, raw): trims; lowercases email and validates it with MailAddress.TryCreate; for phone keeps a leading + and digits, 10-15 digits. Otherwise InvalidGuardianContactError. Mask(): 'g***@example.com' or '******3210'.

Guardian: static Enroll(userId, nowUtc) with Id = userId and EnrolledAtUtc. No Email/Phone/FullName: PII stays in Identity (data minimisation), and the web's 'guardianId = session.userId' assumption now actually holds. Raises GuardianEnrolledEvent(GuardianId, OccurredAtUtc).

Events implement IDomainEvent with an explicit OccurredAtUtc (Consent style), not DomainEvent's DateTime.UtcNow default.

GuardianDbContext:
- HasDefaultSchema('guardian'); Guardians(Id PK ValueGeneratedNever, EnrolledAtUtc).
- GuardianLinks: enums stored as strings; Contact as an owned or complex type (Contact_Channel, Contact_Destination varchar(320) null); ContactHint varchar(64); Challenge owned (Code_Hash varchar(128), Code_Salt varchar(64), Code_IssuedAtUtc, Code_ExpiresAtUtc, Code_FailedAttempts, Code_IssueCount).
- xmin as uint IsRowVersion (D8).
- FK GuardianId to Guardians (Restrict).
- Indexes: IX(CandidateId); IX(GuardianId); unique (CandidateId, GuardianId) HasFilter("\"Status\" = 'Verified'"); unique (CandidateId, Contact_Destination) HasFilter("\"Status\" = 'Pending'").
- Ignore DomainEvents; keep PR2's ApplyUtcDateTimeConversion/ApplyClientGeneratedGuidKeys.

Migration GuardianLinkVerification: drop and recreate both guardian tables. Old rows are dev-only and could never be verified; this is stated in the PR description.
TESTS: GuardianLinkTests.Request_CreatesPendingLink_WithMaskedHint_AndEventWithoutDestination
  GuardianLinkTests.CheckCode_Matching_BeforeExpiry_VerifiesSetsGuardianAndMethod_ClearsDestination_RaisesVerifiedEvent
  GuardianLinkTests.CheckCode_WrongCode_IncrementsFailedAttempts_ReturnsMismatch
  GuardianLinkTests.CheckCode_AfterFiveFailures_ReturnsLockedOut_EvenWithCorrectCode (brute-force protection)
  GuardianLinkTests.CheckCode_FifthFailure_RaisesVerificationLockedEvent
  GuardianLinkTests.CheckCode_AfterExpiry_ReturnsExpired
  GuardianLinkTests.CheckCode_ByCandidateThemselves_ThrowsGuardianCannotLinkSelfError
  GuardianLinkTests.CheckCode_OnVerifiedOrRevoked_ReturnsNotPending
  GuardianLinkTests.ReissueCode_WithinCooldown_Throws / AfterMaxIssues_Throws / ResetsAttemptsAndExpiry
  GuardianLinkTests.AttestByStaff_WithoutReason_Throws / OnPending_VerifiesWithStaffAttestation
  GuardianLinkTests.Revoke_Verified_SetsRevokedAndRaisesEvent / Revoke_AlreadyRevoked_ThrowsNotPending
  GuardianContactTests.Email_IsTrimmedAndLowercased / InvalidEmail_Throws / Phone_TooShort_Throws / Mask_NeverContainsFullDestination

## C7 feat(guardian): link request, OTP verification, revocation and staff attestation use cases (FR-45)
FILES: apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Ports/IGuardianLinkRepository.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Ports/IGuardianRepository.cs (new: GetByIdAsync(userId), AddAsync)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Ports/IGuardianCodeService.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Ports/IGuardianVerificationSender.cs (new) + GuardianVerificationMessage.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/{RequestGuardianLink,ResendGuardianLinkCode,CancelGuardianLinkRequest,VerifyGuardianLink,RevokeGuardianLink,AttestGuardianLink}{Command,Handler}.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Queries/{ListCandidateGuardianLinks,GetGuardianOverview,ListGuardianLinksForStaff}{Query,Handler}.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/GuardianshipQuery.cs (new : IGuardianshipQuery)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/GuardianEnrollment.cs (new helper: ensure a Guardian profile exists)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Dtos/{CandidateGuardianLinkDto,GuardianLinkForGuardianDto,GuardianOverviewDto,StaffGuardianLinkDto}.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Exceptions/{NotEligibleGuardianError(403),CandidateNotFoundError(404),GuardianLinkNotRequiredError(409),GuardianLinkRequestLimitError(429),GuardianLinkAlreadyPendingError(409),GuardianAlreadyLinkedError(409),ConcurrentGuardianLinkUpdateError(409)}.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/ExamPlatform.Modules.Guardian.Application.csproj (+refs Guardian.Contracts, Identity.Contracts)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/Repositories/GuardianLinkRepository.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/Repositories/GuardianRepository.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianUnitOfWork.cs (translate DbUpdateConcurrencyException and PostgresException 23505 into typed errors)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/HmacGuardianCodeService.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianVerificationOptions.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/LoggingGuardianVerificationSender.cs (new, DEV ONLY)
  apps/api/src/Host/ExamPlatform.Api/appsettings.Development.json (Guardian:VerificationCodePepper dev value, Guardian:LogCodesInDevelopment=true)
  .env.example (Guardian__VerificationCodePepper), if present
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/{RequestGuardianLinkHandlerTests,VerifyGuardianLinkHandlerTests,AttestGuardianLinkHandlerTests,RevokeGuardianLinkHandlerTests,HmacGuardianCodeServiceTests}.cs (new; add Guardian.Infrastructure + Identity.Contracts refs to the test csproj)
DETAILS: All handlers are plain sealed classes with HandleAsync(command, ct) (D1). Actor ids always come in via commands populated from the JWT 'sub' claim (D2). Time comes from the injected Clock.

RequestGuardianLinkHandler (CandidateId, RequestedById, RequesterKind, Channel, Destination):
- IUserDirectory.FindAsync(candidate). Missing: CandidateNotFoundError. Adult: GuardianLinkNotRequiredError (why: guardians act only for minors).
- More than 3 pending links for the candidate: GuardianLinkRequestLimitError (anti-spam, SMS pumping).
- A pending link to the same normalized destination: GuardianLinkAlreadyPendingError (hint: use resend).
- codeService.Generate() returns (Code, Salt, Hash); GuardianLink.Request; repository.AddAsync; SaveChanges; then sender.SendAsync(new GuardianVerificationMessage(LinkId, Channel, Destination, Code, CandidateDisplayName, ExpiresAtUtc)).
- Save happens before send so a delivered code always refers to a persisted link. A send failure propagates (not swallowed) and the user can resend.
- The plaintext code exists only in memory and in the sender call.

Resend: the caller must be link.CandidateId, or staff (flag set by the admin endpoint); otherwise GuardianLinkNotFoundError (404 so ids cannot be enumerated). ReissueCode, save, send.

Cancel: the candidate revokes their own Pending link. A Verified link cannot be cancelled by the candidate (GuardianLinkNotPendingError); why-comment: only the guardian or staff can dissolve a verified guardianship.

VerifyGuardianLinkHandler (LinkId, GuardianUserId, Code):
- Load the tracked link (404).
- IUserDirectory.FindAsync(caller) must be IsInRole(Guardian) and Adult, else NotEligibleGuardianError. The check uses the DB, not claims.
- IsVerified(candidate, caller) already true: GuardianAlreadyLinkedError.
- outcome = link.CheckCode(codeService.Hash(code, link.Challenge.CodeSalt), caller, now).
- On Verified: GuardianEnrollment.EnsureAsync(caller), which adds a Guardian profile if none exists.
- ALWAYS SaveChanges, then map a non-Verified outcome to GuardianCodeMismatchError, GuardianCodeExpiredError, GuardianCodeAttemptsExceededError or GuardianLinkNotPendingError. A why-comment explains that the attempt must be persisted before throwing.
- This replaces the NotImplementedException at GuardianHandlers.cs:62.

Revoke: the guardian may revoke a link where link.GuardianId == caller; staff may revoke any link; otherwise 404.

Attest (staff): the guardian user must exist, be in the Guardian role, be Adult and not be the candidate; not already verified; link.AttestByStaff(reason); ensure the profile; save.

Queries:
- List for candidate: guardian display names via IUserDirectory.FindManyAsync, only for Verified links. Pending links show only ContactHint.
- Guardian overview: candidate display names for the guardian's Verified links only.
- Staff list by candidateId.

GuardianshipQuery implements the Contracts port via repository queries (AsNoTracking): verified guardian ids for the candidate and whether any request is pending.

GuardianLinkRepository.GetByIdAsync is tracked; the list methods use AsNoTracking. There are no child collections, so the PR2 Include concern no longer applies.

HmacGuardianCodeService:
- Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"); Salt = 16 random bytes, base64.
- Hash = hex(HMACSHA256(pepperBytes, salt + ':' + code)).
- Why: unsalted SHA-256 over a 6-digit space is reversible from a DB dump (audit NFR-5); the per-link salt plus a config pepper prevents that.
- If PR1 moved a salted OTP primitive into SharedKernel, delegate to it instead.

GuardianVerificationOptions: Pepper is required, at least 32 chars, validated with ValidateDataAnnotations + ValidateOnStart. LogCodesInDevelopment defaults to false.

LoggingGuardianVerificationSender: logs Channel, the masked ContactHint and LinkId. It logs the code only when LogCodesInDevelopment is true, otherwise '[redacted]'. A doc comment says it must be replaced by the Notifications adapter (FR-39) before deployment (NFR-6; mirrors PR1's handling of LoggingOtpSender).

GuardianUnitOfWork: DbUpdateConcurrencyException becomes ConcurrentGuardianLinkUpdateError, which stops parallel code guesses from racing past the attempt cap. PostgresException SqlState 23505 on the partial indexes becomes GuardianAlreadyLinkedError or GuardianLinkAlreadyPendingError, depending on ConstraintName.
TESTS: RequestGuardianLinkHandlerTests.AdultCandidate_ThrowsGuardianLinkNotRequiredError
  RequestGuardianLinkHandlerTests.FourthPendingLink_ThrowsRequestLimitError
  RequestGuardianLinkHandlerTests.HappyPath_SavesBeforeSending_AndSenderReceivesCodeWhoseHashIsStored (Received.InOrder)
  VerifyGuardianLinkHandlerTests.WrongCode_SavesThenThrowsMismatch (proves the attempt is persisted: UoW.SaveChangesAsync received once before the exception)
  VerifyGuardianLinkHandlerTests.CallerWithoutGuardianRole_ThrowsNotEligibleGuardianError
  VerifyGuardianLinkHandlerTests.MinorGuardianAccount_ThrowsNotEligibleGuardianError
  VerifyGuardianLinkHandlerTests.CallerIsCandidate_ThrowsGuardianCannotLinkSelfError
  VerifyGuardianLinkHandlerTests.Success_EnrollsGuardianProfileWhenMissing
  VerifyGuardianLinkHandlerTests.AlreadyVerifiedGuardian_ThrowsGuardianAlreadyLinkedError
  AttestGuardianLinkHandlerTests.TargetUserNotGuardian_Throws / Success_SetsStaffAttestationMethodAndReason
  RevokeGuardianLinkHandlerTests.OtherGuardian_Gets404 / OwnVerifiedLink_Revokes / Staff_RevokesAny
  HmacGuardianCodeServiceTests.SameCodeAndSalt_SameHash / DifferentSalt_DifferentHash / Code_IsSixDigits

## C8 feat(guardian): self-service, guardian and staff link endpoints with RBAC and rate limits (FR-45, FR-2, NFR-5)
FILES: apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianEndpoints.cs (rewrite)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/Requests.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianModuleInstaller.cs
DETAILS: Routes. The actor always comes from PR2's shared GetUserId(); nothing is read from bodies.

Candidate (RequireAuthorization):
- POST /v1/me/guardian-links {channel, destination}: 201 CandidateGuardianLinkDto
- GET /v1/me/guardian-links
- POST /v1/me/guardian-links/{linkId:guid}/resend: 204
- DELETE /v1/me/guardian-links/{linkId:guid}: 204 (cancel a pending link)

Guardian (RequireAuthorization; role eligibility is checked in the handlers via IUserDirectory):
- GET /v1/guardians/me: GuardianOverviewDto
- POST /v1/guardians/me/links/{linkId:guid}/verify {code}: 200 GuardianLinkForGuardianDto
- DELETE /v1/guardians/me/links/{linkId:guid}: 204

Staff (RequireAuthorization("permission:guardian.link.manage")):
- POST /v1/admin/guardian-links {candidateId, channel, destination} (roster/staff-initiated)
- GET /v1/admin/guardian-links?candidateId=
- POST /v1/admin/guardian-links/{linkId:guid}/attest {guardianUserId, reason}
- POST /v1/admin/guardian-links/{linkId:guid}/resend
- DELETE /v1/admin/guardian-links/{linkId:guid}

The request, resend and verify endpoints get .RequireRateLimiting(<PR1's OTP policy name>) in addition to the domain-level caps. Channel strings are parsed with Enum.TryParse + IsDefined into InvalidGuardianContactError (never Enum.Parse; a 500 would otherwise leak). Results.Created points only at routes that exist.

The installer registers: repositories, UoW, IGuardianCodeService (singleton), options bound from the 'Guardian' section, IGuardianVerificationSender (scoped LoggingGuardianVerificationSender), every handler, and IGuardianshipQuery (scoped GuardianshipQuery). Docstrings cite FR-45, not the old FR-22..24 mislabel (GuardianEndpoints.cs:8).
TESTS: Covered by the integration commit (GuardianConsentFlowTests, GuardianLinkAuthorizationTests)

## C9 feat(guardian): audit guardian link lifecycle through domain event handlers (FR-40)
FILES: apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Audit/GuardianLinkAuditHandlers.cs (new: IDomainEventHandler<GuardianLinkRequestedEvent|GuardianLinkVerifiedEvent|GuardianLinkVerificationLockedEvent|GuardianLinkRevokedEvent>)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/ExamPlatform.Modules.Guardian.Application.csproj (+ref Admin.Contracts, if PR2 has not added it)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianModuleInstaller.cs (register handlers)
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/GuardianLinkAuditHandlersTests.cs (new)
DETAILS: Follows PR2's D7 audit-handler pattern (reuse its base class or helper if one exists). Actions: 'GuardianLink.Requested', 'GuardianLink.Verified', 'GuardianLink.VerificationLocked', 'GuardianLink.Revoked'. EntityType 'GuardianLink', EntityId = link id. ActorRole: 'Candidate' / 'Staff' / 'Guardian', derived from the RequesterKind or verification method in the event. Metadata: candidateId, channel, method, and the attestation reason for StaffAttestation. It never contains the destination or the code (NFR-6). The staff manual override is therefore always visible in GET /v1/admin/audit-logs.
TESTS: GuardianLinkAuditHandlersTests.Verified_ByStaffAttestation_RecordsMethodAndReason_WithoutDestination
  GuardianLinkAuditHandlersTests.Requested_RecordsChannelOnly

## C10 feat(consent): enforce ledger integrity, record who gave consent (FR-44)
FILES: apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Domain/ConsentGivenByKind.cs (new: Subject|Guardian|Staff)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Domain/ConsentPurposePolicy.cs (new)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Domain/ConsentRecord.cs (+GivenByKind; Grant(..., givenByKind, nowUtc))
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Domain/Events/ConsentGrantedEvent.cs (+GivenById, GivenByKind)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/ConsentDbContext.cs
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/Repositories/ConsentRecordRepository.cs
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/Repositories/NoticeVersionRepository.cs
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/Ports/INoticeVersionRepository.cs (GetCurrentAsync(purpose, asOfUtc, ct))
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/ConsentUnitOfWork.cs (translate 23505 on IX_ConsentRecords_Active into ConsentAlreadyActiveError, DbUpdateConcurrencyException into ConsentConcurrentUpdateError)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/Exceptions/{ConsentAlreadyActiveError(409),ConsentConcurrentUpdateError(409)}.cs (new)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/ConsentService.cs (pass ConsentGivenByKind.Subject for now; clock passed to GetCurrentAsync)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/Migrations/<ts>_ConsentLedgerIntegrity.cs + Designer + snapshot (generated, then hand-edited for the dedupe SQL)
  apps/api/tests/Modules/Consent/ExamPlatform.Modules.Consent.UnitTests/ConsentRecordTests.cs, ConsentPurposePolicyTests.cs (new)
DETAILS: ConsentPurposePolicy.RequiresGuardianConsentForMinor(purpose) returns true for all three current purposes. The why-comment cites section 7.1 (verifiable parental consent before processing a child's data) and notes that a minor cannot enter the ToS contract; counsel must confirm. It is data-driven so adding a purpose is additive (OCP).

DbContext:
- GivenByKind stored as string(20) with default 'Subject' for existing rows.
- HasOne<NoticeVersion>().WithMany().HasForeignKey(r => r.NoticeVersionId).OnDelete(Restrict) (audit: no FK at InitialCreate:18).
- Unique index IX_ConsentRecords_Active on (SubjectId, Purpose) with HasFilter("\"WithdrawnAtUtc\" IS NULL"). This prevents duplicate active grants (ConsentRecordRepository.cs:15).
- xmin as uint IsRowVersion on ConsentRecord (concurrent withdraws).

Repositories:
- GetActiveAsync orders by GrantedAtUtc desc.
- NoticeVersionRepository.GetCurrentAsync filters EffectiveFromUtc <= asOfUtc before ordering (fixes future-dated notices, NoticeVersionRepository.cs:16).

Migration ConsentLedgerIntegrity, in order:
1. Add the GivenByKind column.
2. Run migrationBuilder.Sql to withdraw older duplicate active rows (keep the latest GrantedAtUtc per SubjectId+Purpose; set WithdrawnAtUtc = now() at time zone 'utc' and WithdrawnById = GivenById). This lets the unique index build on existing dev data.
3. Create the FK and index.
TESTS: ConsentRecordTests.Grant_RecordsGivenByKindAndGivenById_InEvent
  ConsentPurposePolicyTests.AllCurrentPurposes_RequireGuardianForMinors
  Integration (later commit): RecordConsent_Twice_Returns409_ConsentAlreadyActive proves the partial unique index plus typed translation

## C11 fix(consent): authorize every consent read/grant/withdraw by subject, verified guardian or consent.manage (FR-43, FR-44, FR-45)
FILES: apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/ConsentActor.cs (new)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/ConsentGivenByKind.cs (new mirror)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/GuardianConsentRequiredError.cs (new, 403 guardian_consent_required)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/Dtos.cs
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/IConsentService.cs
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/ConsentAccessPolicy.cs (new)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/EffectiveConsentEvaluator.cs (new)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/ConsentService.cs
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/Exceptions/{ConsentAccessDeniedError(403),ConsentSubjectNotFoundError(404),NoticeVersionPurposeMismatchError(400),NoticeVersionNotCurrentError(409)}.cs (new)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/IConsentUnitOfWork.cs (+ExecuteInTransactionAsync for supersede)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/ConsentUnitOfWork.cs
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/ExamPlatform.Modules.Consent.Application.csproj (+refs Identity.Contracts, Guardian.Contracts)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Endpoints/ConsentEndpoints.cs
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Endpoints/ConsentModuleInstaller.cs (register policy and evaluator)
  apps/api/tests/Modules/Consent/ExamPlatform.Modules.Consent.UnitTests/ConsentAccessPolicyTests.cs (new)
  apps/api/tests/Modules/Consent/ExamPlatform.Modules.Consent.UnitTests/ConsentServiceTests.cs (update and extend; add Identity.Contracts/Guardian.Contracts refs)
DETAILS: Fixes the high-severity IDOR (ConsentEndpoints.cs:17-35, ConsentService.cs:30-60) and the notice check gap (ConsentService.cs:41).

Contracts:
- ConsentActor(Guid UserId, bool CanManageConsent), built only from JWT claims by the endpoints.
- RecordConsentRequest(SubjectId, Purpose, NoticeVersionId): GivenById is removed; the actor gives it.
- IConsentService: RecordConsentAsync(request, actor, ct), WithdrawConsentAsync(recordId, actor, ct), GetStatusAsync(subjectId, purpose, actor, ct).
- HasActiveConsentAsync and EnsureConsentAsync stay actor-less, with a doc comment: trusted server-side callers only, never expose over HTTP.
- ConsentStatusDto gains RequiresGuardianConsent, IsEffective, GivenById?, GivenByKind?, CurrentNoticeVersionLabel?, CurrentNoticeContentReference? (audit gap Dtos.cs:12-13).
- ConsentRecordDto gains GivenById, GivenByKind, WithdrawnById (Dtos.cs:22-28).

ConsentAccessPolicy uses IUserDirectory and IGuardianshipQuery. Every rule gets a why-comment (section 1.3 requires them for consent rules).
- Read: the subject, a verified guardian of the subject, or CanManageConsent. Otherwise ConsentAccessDeniedError.
- Grant, in order:
  - Subject missing: ConsentSubjectNotFoundError.
  - Actor == subject: if Minor and the policy requires a guardian, GuardianConsentRequiredError; otherwise kind Subject.
  - Verified guardian: the subject must be Minor, else ConsentAccessDeniedError (guardians do not consent for adults); kind Guardian.
  - CanManageConsent: a minor subject gives GuardianConsentRequiredError (staff cannot stand in for verifiable parental consent; the override route is guardian-link attestation); otherwise kind Staff.
  - Anyone else: denied.
- Withdraw: the subject (a minor too; withdrawing only reduces processing), a verified guardian of a minor subject, or CanManageConsent.

ConsentService.RecordConsentAsync:
- notice = GetByIdAsync, else NoticeVersionNotFoundError.
- notice.Purpose != request.Purpose: NoticeVersionPurposeMismatchError.
- notice.Id != GetCurrentAsync(purpose, clock.UtcNow).Id: NoticeVersionNotCurrentError.
- kind = policy.AuthorizeGrant.
- An existing active record on the same notice gives ConsentAlreadyActiveError. One on an older notice is superseded: withdraw it (actor, now) and grant the new one inside UoW.ExecuteInTransactionAsync with two SaveChanges, so the partial unique index never sees two active rows.
- ActorRole in audit = kind.

EffectiveConsentEvaluator (shared with the gate):
- Minor: an active record counts only if GivenByKind == Guardian and GivenById is among the currently verified guardians.
- Adult: an active record counts if GivenByKind is Subject or Staff. Why: consent given by a parent on behalf of a child must be re-given by the adult; this is an assumption flagged for counsel.

Endpoints:
- GET /v1/consent/status?subjectId (optional, defaults to the caller)&purpose.
- POST /v1/consent with body SubjectId optional (defaults to the caller).
- DELETE /v1/consent/{id}.
- The actor is built via PR2's GetUserId() and HasPermission('consent.manage').
- Delete the private CallerId (ConsentEndpoints.cs:28), which used Guid.Parse on a null-forgiving claim.
TESTS: ConsentAccessPolicyTests (Theory matrix): AdultSelfGrant_Allowed_KindSubject; MinorSelfGrant_ThrowsGuardianConsentRequired; VerifiedGuardianGrantForMinor_Allowed_KindGuardian; UnverifiedOrUnrelatedUser_ThrowsAccessDenied; GuardianGrantForAdult_ThrowsAccessDenied; StaffGrantForMinor_ThrowsGuardianConsentRequired; StaffGrantForAdult_Allowed_KindStaff; StrangerRead_Denied; MinorSelfWithdraw_Allowed; UnknownSubject_ThrowsNotFound
  ConsentServiceTests.RecordConsent_NoticeOfDifferentPurpose_ThrowsPurposeMismatch
  ConsentServiceTests.RecordConsent_NonCurrentNotice_ThrowsNotCurrent
  ConsentServiceTests.RecordConsent_ActiveOnSameNotice_ThrowsAlreadyActive
  ConsentServiceTests.RecordConsent_ActiveOnOlderNotice_SupersedesInTransaction
  ConsentServiceTests.GetStatus_MinorWithSelfGivenLegacyRecord_IsActiveButNotEffective
  ConsentServiceTests.RecordConsentAsync_PersistsAndEmitsConsentGrantedEvent (updated signature)

## C12 fix(consent): audit grants and withdrawals atomically via a consent-schema audit outbox (FR-40, FR-44)
FILES: apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/Ports/IConsentAuditTrail.cs (new: stage an AuditEntry in the current unit of work)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/Ports/IConsentAuditOutboxStore.cs (new: GetPendingAsync(batch), MarkDispatched, MarkFailed)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/ConsentAuditRelay.cs (new: forwards pending entries to Admin.Contracts IAuditLogger)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/ConsentService.cs (stage audit before SaveChanges; relay after commit; drop the direct IAuditLogger calls at :62-71)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/Audit/ConsentAuditOutboxMessage.cs (new persistence entity)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/Audit/ConsentAuditTrail.cs (new: adds a row with a System.Text.Json payload)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/Audit/ConsentAuditOutboxStore.cs (new)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/ConsentDbContext.cs (+DbSet AuditOutbox, table consent.AuditOutbox, index on DispatchedAtUtc)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/Migrations/<ts>_ConsentAuditOutbox.cs + Designer + snapshot
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Endpoints/ConsentAuditOutboxHostedService.cs (new BackgroundService, PeriodicTimer 30 s)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Endpoints/ConsentModuleInstaller.cs (register trail, store, relay, AddHostedService)
  apps/api/src/Modules/Admin/ExamPlatform.Modules.Admin.Contracts/AuditWriteFailedException.cs (new, infrastructure failure type)
  apps/api/src/Modules/Admin/ExamPlatform.Modules.Admin.Infrastructure/AdminUnitOfWork.cs (wrap DbUpdateException/NpgsqlException into AuditWriteFailedException, still logged and rethrown)
  apps/api/tests/Modules/Consent/ExamPlatform.Modules.Consent.UnitTests/ConsentServiceAuditTests.cs, ConsentAuditRelayTests.cs (new)
DETAILS: Why: WithdrawConsentAsync currently commits (ConsentService.cs:60) and only then writes the audit entry through a different DbContext (:62). A failed audit write then leaves an unaudited change plus a 500, and grants are not audited at all (:39-51). IUnitOfWork's doc forbids cross-module transactions, so the audit fact is written into Consent's own schema in the same SaveChanges as the ledger change (atomic by construction), then relayed.

ConsentService stages 'Consent.Granted' (and 'Consent.Withdrawn' for a supersede) or 'Consent.Withdrawn' with: ActorUserId = actor, ActorRole = GivenByKind, EntityType 'ConsentRecord', and Metadata purpose, noticeVersionId, givenByKind, subjectId, outboxMessageId. After commit it calls ConsentAuditRelay.DispatchPendingAsync.

The relay catches only AuditWriteFailedException. On failure it logs an error with the message id and action, increments Attempts, records LastError (the type name only), and leaves the row pending. A request whose ledger change committed is therefore never turned into a 500. The hosted service retries every 30 s and never swallows: it logs each failure with context.

Delivery is at-least-once. Duplicates can be detected by the outboxMessageId metadata. If PR2 shipped a generic SharedKernel outbox, register these entries with it instead and skip the Consent-local table, relay and hosted service. The Application-layer relay depends only on Admin.Contracts, so the ADR reference rules hold (Infrastructure never references another module's Contracts).
TESTS: ConsentServiceAuditTests.Grant_StagesConsentGrantedAuditBeforeSave (Received.InOrder: trail.Record then UoW.SaveChangesAsync)
  ConsentServiceAuditTests.Withdraw_StagesConsentWithdrawnBeforeSave_WithActorRole
  ConsentAuditRelayTests.AuditLoggerFails_MarksFailedAndDoesNotThrow
  ConsentAuditRelayTests.Success_MarksDispatched
  Integration: AuthConsentAuditFlowTests asserts both Consent.Granted and Consent.Withdrawn audit entries

## C13 feat(consent): add IConsentGate 'may this candidate proceed' and GET /v1/me/exam-eligibility (FR-43, S11-IConsentService)
FILES: apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/IConsentGate.cs (new)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/CandidateEligibilityDto.cs (new)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/ConsentRequirements.cs (new: ExamAccessBaseline = [TermsOfService, PrivacyNotice])
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/ConsentGate.cs (new)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/Ports/IConsentRecordRepository.cs (+ListActiveForSubjectAsync)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/Repositories/ConsentRecordRepository.cs
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Endpoints/ConsentEndpoints.cs (GET /v1/me/exam-eligibility)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Endpoints/ConsentModuleInstaller.cs (AddScoped<IConsentGate, ConsentGate>)
  apps/api/tests/Modules/Consent/ExamPlatform.Modules.Consent.UnitTests/ConsentGateTests.cs (new)
DETAILS: Contract:
- MayCandidateProceedAsync(Guid candidateId, IReadOnlyCollection<ConsentPurpose> requiredPurposes, CancellationToken) returns CandidateEligibilityDto(CandidateId, IsMinor, IsEligible, RequiresGuardianConsent, HasVerifiedGuardian, HasPendingGuardianRequest, MissingPurposes).
- EnsureCandidateMayProceedAsync(...) throws GuardianConsentRequiredError for a minor or ConsentRequiredError for an adult.
- It is a separate interface from IConsentService (ISP). Together the two implement section 11's IConsentService responsibility; the doc comment says so.
- IsMinor is a bool because Consent.Contracts must not reference Identity.Contracts.

ConsentGate: IUserDirectory (missing user gives ConsentSubjectNotFoundError), IGuardianshipQuery.GetForCandidateAsync, the ledger via ListActiveForSubjectAsync, and EffectiveConsentEvaluator. MissingPurposes lists the required purposes without effective consent. A doc comment states that M4's POST /exams/{id}/attempts must call EnsureCandidateMayProceedAsync, and that invite acceptance is deliberately not gated (a minor may enrol, then obtain consent before starting).

GET /v1/me/exam-eligibility is authenticated and uses candidateId = sub and ConsentRequirements.ExamAccessBaseline. Proctoring purposes are added per exam profile in M6. The why-comment covers the section 7.1 child definition and the verifiable-parental-consent rule.
TESTS: ConsentGateTests.Adult_WithBothSelfGivenConsents_IsEligible
  ConsentGateTests.Adult_WithOnlyGuardianGivenConsentFromMinority_IsNotEligible
  ConsentGateTests.Minor_WithoutVerifiedGuardian_NotEligible_RequiresGuardian
  ConsentGateTests.Minor_WithSelfGivenLegacyConsent_NotEligible
  ConsentGateTests.Minor_WithConsentFromCurrentlyVerifiedGuardian_IsEligible
  ConsentGateTests.Minor_WithConsentFromRevokedGuardian_NotEligible (compliance)
  ConsentGateTests.Ensure_Minor_ThrowsGuardianConsentRequiredError / Ensure_Adult_ThrowsConsentRequiredError
  ConsentGateTests.UnknownCandidate_ThrowsSubjectNotFound

## C14 feat(consent): add authorized consent history GET /v1/consent/records (FR-44, FR-45)
FILES: apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/IConsentService.cs (+ListRecordsAsync(subjectId, actor, ct))
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/ConsentService.cs
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/Ports/IConsentRecordRepository.cs (+ListForSubjectAsync ordered GrantedAtUtc desc)
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/Repositories/ConsentRecordRepository.cs
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Endpoints/ConsentEndpoints.cs
  apps/api/tests/Modules/Consent/ExamPlatform.Modules.Consent.UnitTests/ConsentServiceTests.cs
DETAILS: Fills the FR-44 gap 'no endpoint to read the ledger history' and supports the FR-45 guardian 'view'. Same read authorization as status (ConsentAccessPolicy.AuthorizeReadAsync). subjectId defaults to the caller.
TESTS: ConsentServiceTests.ListRecords_Stranger_ThrowsAccessDenied / VerifiedGuardian_ReturnsHistoryNewestFirst

## C15 feat(guardian): guardian gives, views and withdraws consent for a linked minor (POST /v1/guardians/consent) (FR-45, S13)
FILES: apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/GiveGuardianConsent{Command,Handler}.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/WithdrawGuardianConsent{Command,Handler}.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Queries/GetCandidateConsentForGuardian{Query,Handler}.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Exceptions/NotAVerifiedGuardianError.cs (new, 403 not_a_verified_guardian)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/ExamPlatform.Modules.Guardian.Application.csproj (+ref Consent.Contracts)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianEndpoints.cs (+3 routes)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/Requests.cs (+GuardianConsentRequest(CandidateId, Purpose, NoticeVersionId))
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianModuleInstaller.cs
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/GiveGuardianConsentHandlerTests.cs (new)
DETAILS: Routes:
- POST /v1/guardians/consent {candidateId, purpose, noticeVersionId}: 200 ConsentRecordDto
- DELETE /v1/guardians/consent/{consentRecordId:guid}: 204
- GET /v1/guardians/me/candidates/{candidateId:guid}/consent: one ConsentStatusDto per ConsentPurpose

The grant and status handlers pre-check own data (a Verified link for the caller and candidate) and throw NotAVerifiedGuardianError for a clear error. They then delegate to Consent.Contracts IConsentService with ConsentActor(callerId, CanManageConsent: false). Consent re-checks through Guardian.Contracts, so there is a single enforcement point plus defence in depth, and the ledger stores GivenById = guardian user id and GivenByKind = Guardian.

Withdraw delegates directly, because Consent derives the subject from the record. No DI cycle: GuardianshipQuery does not depend on IConsentService.
TESTS: GiveGuardianConsentHandlerTests.NotVerifiedGuardian_ThrowsNotAVerifiedGuardianError_AndNeverCallsConsent
  GiveGuardianConsentHandlerTests.VerifiedGuardian_CallsRecordConsentWithGuardianActor

## C16 test(arch): enforce boundaries for Identity/Guardian contracts and the Consent-Guardian dependency (D6)
FILES: apps/api/tests/ExamPlatform.ArchitectureTests/ApplicationLayerTests.cs
  apps/api/tests/ExamPlatform.ArchitectureTests/ContractsLayerTests.cs
  apps/api/tests/ExamPlatform.ArchitectureTests/InfrastructureLayerTests.cs
  apps/api/tests/ExamPlatform.ArchitectureTests/ExamPlatform.ArchitectureTests.csproj
DETAILS: Negative rules:
- Consent.Application must not depend on Guardian or Identity Domain/Application/Infrastructure.
- Guardian.Application must not depend on Consent or Identity internals.
- Consent.Infrastructure and Guardian.Infrastructure must not depend on any other module (not even Contracts).
- The Contracts module-name deny lists include Identity and Guardian.

Positive controls, in the style of IdentityApplication_MayDependOnAdminContracts:
- ConsentApplication_DependsOnGuardianAndIdentityContracts
- GuardianApplication_DependsOnConsentContracts

The rows are added to PR2's all-module lists.
TESTS: All ArchitectureTests pass

## C17 test(integration): guardian OTP link, guardian consent, consent IDOR and FR-43 gate flows (FR-43, FR-44, FR-45)
FILES: apps/api/tests/ExamPlatform.IntegrationTests/CapturingGuardianVerificationSender.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/ApiFactory.cs (register the capturing guardian sender; set Guardian:VerificationCodePepper explicitly)
  apps/api/tests/ExamPlatform.IntegrationTests/TestUsers.cs (new helper: register via /v1/auth/register with dob/accountType, verify OTP, return token and userId; staff tokens via PR2's TestJwtTokenBuilder perm claims)
  apps/api/tests/ExamPlatform.IntegrationTests/GuardianConsentFlowTests.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/ConsentAuthorizationTests.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/GuardianLinkAuthorizationTests.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/AuthConsentAuditFlowTests.cs (assert Consent.Granted too)
DETAILS: The tests use real HTTP against the real Host and Testcontainers Postgres, in the existing *FlowTests style with JsonSerializerOptions(Web) plus JsonStringEnumConverter. Candidates and guardians get real tokens through registration, so the server-side role and age checks are exercised. Staff get tokens with 'perm' claims.
TESTS: GuardianConsentFlowTests.MinorCandidate_GuardianOtpLinkThenGuardianConsent_MakesCandidateEligible. Steps: register a minor (DOB 2012-05-01); GET /v1/me/exam-eligibility shows not eligible and requiresGuardianConsent; the minor's POST /v1/consent returns 403 guardian_consent_required; POST /v1/me/guardian-links to parent@example.com and capture the code; register parent@example.com with accountType Guardian (adult) and verify OTP; POST verify with a wrong code gives 400 and the DB shows Code_FailedAttempts=1 (attempt persisted); the correct code gives Verified; POST /v1/guardians/consent for TermsOfService and PrivacyNotice; eligibility is now eligible; the DB row has GivenById == guardian userId and GivenByKind Guardian; the audit log has GuardianLink.Verified and Consent.Granted
  GuardianConsentFlowTests.GuardianRevokesLink_CandidateBecomesIneligible (consent from a no-longer-verified guardian stops counting)
  GuardianConsentFlowTests.VerifyWithFiveWrongCodes_ThenCorrectCode_Returns429AttemptsExceeded
  GuardianConsentFlowTests.CodeNeverAppearsInApiResponses (request/list DTOs have no code or full destination)
  GuardianLinkAuthorizationTests.VerifyByUserWithoutGuardianRole_Returns403 / VerifyByCandidateThemselves_Returns403
  GuardianLinkAuthorizationTests.AdminEndpoints_WithCandidateToken_Return403 / StaffAttest_WithPermission_Returns200_AndAuditHasMethodAndReason
  GuardianLinkAuthorizationTests.OtherCandidateCannotResendOrCancelMyLink_Returns404
  GuardianLinkAuthorizationTests.RegisterWithAccountTypeSuperAdmin_Returns400 / RegisterGuardianWithMinorDob_Returns400
  ConsentAuthorizationTests.UserB_GetStatusForUserA_Returns403 / PostForUserA_Returns403 / DeleteUserAsRecord_Returns403 (IDOR fixed)
  ConsentAuthorizationTests.StaffWithConsentManage_CanReadAnyStatus_ButCannotGrantForMinor
  ConsentAuthorizationTests.RecordConsent_WithOtherPurposesNotice_Returns400 / WithFutureOrOldNotice_Returns409 (seed a second NoticeVersion directly in ConsentDbContext)
  ConsentAuthorizationTests.RecordConsent_Twice_Returns409ConsentAlreadyActive
  AuthConsentAuditFlowTests.FullJourney (updated): adult self-consent still works; audit contains Consent.Granted and Consent.Withdrawn

## C18 fix(web): guardian sign-up creates an Identity Guardian account via /v1/auth/register (FR-45)
FILES: apps/web/src/app/auth/auth.models.ts (AccountType = 'Candidate' | 'Guardian'; RegisterCandidateRequest.accountType?; UserProfileDto.ageBand)
  apps/web/src/app/guardian-portal/guardian-register/guardian-register.ts (rewrite: displayName, dateOfBirth, channel, destination)
  apps/web/src/app/guardian-portal/guardian-register/guardian-register.spec.ts (new)
DETAILS: Uses AuthApiService.register({..., accountType: 'Guardian'}), then router.navigate(['/verify-otp'], {queryParams: {challengeId, purpose: 'Registration', destination, returnUrl: '/guardian'}}). verify-otp.ts already honours returnUrl. Removes authSession.login(guardian.id) with a GUID (guardian-register.ts:127) and the misleading success banner. Errors use extractErrorMessage, so e.g. 'guardian_must_be_adult' detail is shown. The route stays public (app.routes.ts:83-86), which is now correct because /v1/auth/register is anonymous.
TESTS: guardian-register.spec: submits accountType Guardian to /v1/auth/register and navigates to /verify-otp with returnUrl=/guardian; never calls AuthSessionService.login; shows ProblemDetails detail on 400

## C19 feat(web): candidate 'request guardian consent' page and guardian verify-code page (FR-45)
FILES: apps/web/src/app/guardian-portal/guardian.models.ts (rewrite to the new DTOs)
  apps/web/src/app/guardian-portal/guardian-api.service.ts (rewrite: requestLink, listMyLinks, resend, cancel, getOverview, verifyLink(linkId, code), revokeLink(linkId), getCandidateConsent, grantConsent, withdrawConsent)
  apps/web/src/app/guardian-portal/guardian-api.service.spec.ts (rewrite URLs)
  apps/web/src/app/guardian-portal/guardian-request/guardian-request.ts (new; replaces guardian-link.ts, deleted)
  apps/web/src/app/guardian-portal/guardian-request/guardian-request.spec.ts (new)
  apps/web/src/app/guardian-portal/guardian-verify/guardian-verify.ts (new)
  apps/web/src/app/guardian-portal/guardian-verify/guardian-verify.spec.ts (new)
  apps/web/src/app/app.routes.ts (add me/guardian and guardian/verify with authGuard; remove guardian/link-candidate)
DETAILS: The candidate page (/me/guardian) lists their links with status and masked ContactHint, and offers a form (channel plus email or phone) to request, plus resend and cancel. It replaces the guardian-link page that sent session.userId as guardianId (guardian-link.ts:118) and falsely claimed a link was sent (:58). The guardian verify page (/guardian/verify?linkId=...) takes a 6-digit code and posts to /v1/guardians/me/links/{linkId}/verify; on success it navigates to /guardian. authGuard keeps returnUrl, so the link in the message works for a signed-out guardian. All errors use extractErrorMessage and render in role=alert containers. The old non-existent GET /v1/guardians/{id} and /{id}/links calls (guardian-api.service.ts:15-17, 35-37) are removed.
TESTS: guardian-api.service.spec: each method hits the exact new URL and verb
  guardian-request.spec: submits channel and destination, shows the hint list, surfaces 429 detail
  guardian-verify.spec: reads linkId from the query, posts the code, navigates to /guardian on success, shows the mismatch detail on 400

## C20 fix(web): guardian dashboard uses /v1/guardians/me instead of the user id as guardian id (FR-45)
FILES: apps/web/src/app/guardian-portal/guardian-dashboard/guardian-dashboard.ts (rewrite with signals)
  apps/web/src/app/guardian-portal/guardian-dashboard/guardian-dashboard.spec.ts (new)
DETAILS: Loads GET /v1/guardians/me and shows linked candidates (display name, status, verified date, method) with a 'Manage consent' link. Revoke goes by linkId. Fixes session.userId used as guardianId (guardian-dashboard.ts:120), the always-failing load (:127-137), and the console.error-only revoke error (:146-148). The empty state explains that links start from the candidate's request.
TESTS: guardian-dashboard.spec: renders links from /v1/guardians/me, revoke calls DELETE /v1/guardians/me/links/{linkId}, shows an error message

## C21 feat(web): guardian gives, views and withdraws consent for a linked minor (FR-45)
FILES: apps/web/src/app/guardian-portal/guardian-candidate-consent/guardian-candidate-consent.ts + .html (new)
  apps/web/src/app/guardian-portal/guardian-candidate-consent/guardian-candidate-consent.spec.ts (new)
  apps/web/src/app/app.routes.ts (guardian/candidates/:candidateId/consent, authGuard)
DETAILS: One row per purpose, showing the effective status, the notice version label, and a link to ContentReference, so the guardian sees what they accept (FR-44 gap: 'notice text is never shown'). Grant posts to /v1/guardians/consent with currentNoticeVersionId. Withdraw calls DELETE /v1/guardians/consent/{id}.
TESTS: guardian-candidate-consent.spec: loads 3 statuses, grant posts candidateId/purpose/noticeVersionId, withdraw calls DELETE

## C22 fix(web): consent page enforces minor rules, shows notice version and exam eligibility (FR-43, FR-44)
FILES: apps/web/src/app/consent/consent.ts, consent.html
  apps/web/src/app/consent/consent.models.ts (extended ConsentStatusDto; RecordConsentRequest.subjectId optional; CandidateEligibilityDto)
  apps/web/src/app/consent/consent-api.service.ts (subjectId optional; getEligibility())
  apps/web/src/app/consent/consent.spec.ts (update and extend)
  apps/web/src/app/profile/profile.html + profile.ts (show age group; link to /me/guardian for minors and to /guardian when roles include Guardian)
DETAILS: consent.ts loads the profile ageBand and GET /v1/me/exam-eligibility.
- Minor: Grant is hidden and replaced by 'Your guardian must give this consent' plus a link to /me/guardian. Withdraw stays available. The server enforces this too (fix for consent.ts:54).
- Every row shows the notice version label and a link to it.
- An eligibility banner lists missing purposes and guardian state.
- The class doc comment is updated (the old 'backend doesn't verify guardian relationships yet' note is now false).

Role-aware nav and route guards stay in PR4. The profile page links are the entry points for now.
TESTS: consent.spec: adult sees Grant (existing test updated to new DTO); minor sees no Grant button and a link to /me/guardian; eligibility banner lists missing purposes

## C23 docs: correct guardian/consent requirement IDs and document the guardian consent API (FR-43, FR-44, FR-45)
FILES: README.md (endpoint table for /v1/me/guardian-links, /v1/guardians/me*, /v1/guardians/consent, /v1/admin/guardian-links*, /v1/consent/records, /v1/me/exam-eligibility; FR-22..24 mislabel changed to FR-43/FR-45; new Guardian:VerificationCodePepper config)
DETAILS: The audit flagged the README and GuardianEndpoints labelling guardian features as FR-22..24, which are proctoring IDs.
TESTS: 

## migrations
- Guardian: <timestamp>_GuardianLinkVerification. Drops and recreates guardian.Guardians as (Id uuid PK = Identity user id, EnrolledAtUtc) and guardian.GuardianLinks with: CandidateId; GuardianId (nullable FK to Guardians, Restrict); Contact_Channel; Contact_Destination (nullable, 320); ContactHint; Status; RequesterKind; RequestedById/AtUtc; VerificationMethod; VerifiedById/AtUtc; AttestationReason; RevokedById/AtUtc; Code_Hash, Code_Salt, Code_IssuedAtUtc, Code_ExpiresAtUtc, Code_FailedAttempts, Code_IssueCount; xmin. Indexes: IX(CandidateId), IX(GuardianId), unique (CandidateId, GuardianId) WHERE Status='Verified', unique (CandidateId, Contact_Destination) WHERE Status='Pending'. Command: dotnet ef migrations add GuardianLinkVerification --project apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure --startup-project apps/api/src/Host/ExamPlatform.Api --context GuardianDbContext --output-dir Migrations. Old data is dropped: dev-only, and no link could ever be verified.
- Consent: <timestamp>_ConsentLedgerIntegrity. Adds ConsentRecords.GivenByKind varchar(20) NOT NULL DEFAULT 'Subject'. A hand-written Sql() step withdraws older duplicate active rows per (SubjectId, Purpose). Adds FK ConsentRecords.NoticeVersionId to NoticeVersions.Id ON DELETE RESTRICT plus its index, the unique filtered index IX_ConsentRecords_Active (SubjectId, Purpose) WHERE "WithdrawnAtUtc" IS NULL, and xmin as the concurrency token. Command: dotnet ef migrations add ConsentLedgerIntegrity --project apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure --startup-project apps/api/src/Host/ExamPlatform.Api --context ConsentDbContext --output-dir Migrations
- Consent: <timestamp>_ConsentAuditOutbox. Creates consent.AuditOutbox (Id uuid PK, CreatedAtUtc, Action varchar(100), PayloadJson jsonb, DispatchedAtUtc null, Attempts int, LastError varchar(200) null) with a filtered index on CreatedAtUtc WHERE DispatchedAtUtc IS NULL. Skip it if PR2 provides a generic outbox.
- Identity and Admin: no schema change. The Guardian role and the guardian.link.manage permission are seeded by PR2's upsert seeder.

## reuse
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/OtpChallenge.cs: design reference only (hashed code, expiry, MaxAttempts, typed failure modes). Guardian re-implements the idea in its own domain because it cannot reference Identity internals, and it fixes the attempt-persistence flaw (the Guardian domain returns an outcome, and the handler saves before throwing).
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/OtpCodeGenerator.cs: CSPRNG 6-digit generation (RandomNumberGenerator.GetInt32 ... ToString("D6")), reused in HmacGuardianCodeService, upgraded to salted HMAC.
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/LoggingOtpSender.cs plus PR1's NFR-6 fix: pattern for the DEV-ONLY LoggingGuardianVerificationSender (masked destination, code only behind a dev flag).
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/User.cs:91-97 GetAgeBand: moved to AgeBandCalculator and exposed via Identity.Contracts IUserDirectory.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/ConsentPurpose.cs and ConsentService.ToDomain/ToContract: the mirror-enum plus explicit switch mapping pattern for Identity.Contracts.AgeBand and ConsentGivenByKind.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/ConsentRequiredError.cs: existing typed 403 for the adult branch of EnsureCandidateMayProceedAsync. GuardianConsentRequiredError sits next to it in Contracts for M4 to catch.
- apps/api/src/Modules/Admin/ExamPlatform.Modules.Admin.Contracts/IAuditLogger.cs + AuditEntry.cs: the audit sink for Guardian event handlers and the Consent outbox relay.
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/Authorization/PermissionPolicyProvider.cs: RequireAuthorization("permission:guardian.link.manage") on /v1/admin/guardian-links.
- PR2's shared ClaimsPrincipal helper (relocated Identity.Endpoints/ClaimsPrincipalExtensions.cs GetUserId): actor ids and the consent.manage check. Replaces ConsentEndpoints.cs CallerId.
- apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/IDomainEventDispatcher.cs (IDomainEventHandler<T>) and SharedKernel.Infrastructure/DomainEventsSaveChangesInterceptor.cs: dispatch of the Guardian audit handlers.
- apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Clock.cs and tests/Modules/Consent/ExamPlatform.Modules.Consent.UnitTests/FakeClock.cs (copied into the Guardian unit tests).
- apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Domain/DomainException.cs + Host/ExamPlatform.Api/DomainExceptionHandler.cs: every new error is a DomainException subclass with its own ErrorCode/HttpStatusCode, with no Host change.
- apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Infrastructure/UtcDateTimeConventions.cs + ClientGeneratedKeyConventions.cs: already used by ConsentDbContext; PR2 adds them to GuardianDbContext.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/ConsentSeeder.cs: existing notice versions. GetCurrentAsync now honours EffectiveFromUtc <= now.
- apps/api/tests/ExamPlatform.IntegrationTests/ApiFactory.cs, CapturingOtpSender.cs (template for CapturingGuardianVerificationSender), TestJwtTokenBuilder.cs, AuthConsentAuditFlowTests.cs (journey style and audit assertions).
- apps/api/tests/ExamPlatform.ArchitectureTests/ContractsLayerTests.cs MemberData rows and ApplicationLayerTests positive-control pattern.
- apps/web/src/app/shared/problem-details.ts extractErrorMessage; apps/web/src/app/auth/auth-api.service.ts register(); auth/verify-otp/verify-otp.ts (already honours returnUrl); auth/auth.guard.ts (keeps returnUrl for the guardian verify link); consent/consent-api.service.ts (extended, reused by guardian consent UI patterns).

## covers
- apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/GuardianHandlers.cs:62: VerifyGuardianLinkHandler throws NotImplementedException (HTTP 500). Replaced by the OTP verify handler.
- apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianEndpoints.cs:15: no authorization or ownership on guardian endpoints. Replaced by /me self-service routes, server-side role and age checks, and permission:guardian.link.manage on staff routes.
- apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Guardian.cs:8-16,24: Guardian not bound to an Identity user (unrelated Guid). Guardian.Id is now the Identity user id, and GuardianLink.GuardianId points to it.
- apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/GuardianHandlers.cs:37: plaintext GUID token, no expiry, never delivered. Replaced by a salted HMAC 6-digit code with 24 h expiry, 5 attempts and resend limits, delivered through IGuardianVerificationSender.
- apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianRepositoryAndUnitOfWork.cs:14,18: missing Include and InvalidOperationException not-found. Superseded: GuardianLink is its own aggregate and not-found is a typed 404.
- apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Guardian.cs:39 and GuardianLink.cs:34,44: InvalidOperationException state errors. Now typed DomainExceptions.
- apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianDbContext.cs:20: no uniqueness constraints. Partial unique indexes on verified and pending links, plus xmin.
- apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Guardian.cs/GuardianLink.cs: DateTime.UtcNow and GuardianLinkVerified/Revoked events never raised. Now nowUtc parameters, and the events are raised and audited (FR-40).
- apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Events/GuardianCreatedEvent.cs:5: PII (email, name) in the event payload. Removed; events carry ids and channel only (NFR-6).
- S10-GuardianLink gap: no 'method' column. VerificationMethod (OtpToGuardianContact|StaffAttestation) added.
- S13-GuardiansConsentAPI: POST /guardians/consent missing. Added (plus withdraw and view).
- FR-45 gap: Guardian never writes to the Consent ledger. Guardian.Application now goes through Consent.Contracts with GivenBy = guardian user id.
- FR-45 gap: no GET endpoints for guardian details or links. Added GET /v1/guardians/me, /v1/me/guardian-links and /v1/admin/guardian-links.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Endpoints/ConsentEndpoints.cs:17-35 + ConsentService.cs:30-60: IDOR on status, grant and withdraw. Fixed by ConsentAccessPolicy (subject / verified guardian / consent.manage).
- IdentitySeeder.cs:25: consent.manage seeded but never enforced. Now enforced in ConsentActor and ConsentAccessPolicy.
- apps/web/src/app/consent/consent.ts:54 plus the server side: a minor can self-grant consent. Server GuardianConsentRequiredError; the UI hides Grant for minors.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/ConsentService.cs:41: notice purpose and current version not checked. NoticeVersionPurposeMismatchError / NoticeVersionNotCurrentError.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/ConsentService.cs:39-51: grant not audited. It is now audited.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/ConsentService.cs:60-71: audit not atomic with the change. The consent-schema outbox is written in the same SaveChanges.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Application/ConsentService.cs:65: ActorRole null. Now set from GivenByKind.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/Repositories/ConsentRecordRepository.cs:15: duplicate active grants and unordered FirstOrDefault. Partial unique index, ConsentAlreadyActiveError, ordered query.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/Repositories/NoticeVersionRepository.cs:16: future-dated notice treated as current. Filtered by EffectiveFromUtc <= now.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Infrastructure/Migrations/20260929104032_InitialCreate.cs:18: no FK from NoticeVersionId. FK added.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Endpoints/ConsentEndpoints.cs:28: duplicated CallerId with Guid.Parse on a null-forgiving claim. Replaced by the shared helper.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/Dtos.cs:12-13,22-28: no notice metadata, GivenById or WithdrawnById in the DTOs. Added.
- FR-44 gap: no ledger history endpoint. GET /v1/consent/records.
- S11-IConsentService / IConsentService.cs:10-41: no 'may this candidate proceed' decision (age band, guardian verification). IConsentGate plus GET /v1/me/exam-eligibility.
- FR-43 gap: Identity has no Contracts and age band is not exposed (not in UserProfileDto). Identity.Contracts IUserDirectory, and ageBand on /v1/me/profile.
- FR-43 gap: 'No guardian consent purpose / cannot tell that a minor's consent came from a verified guardian'. GivenByKind plus the EffectiveConsentEvaluator rule.
- IdentitySeeder.cs:42: Guardian role never assigned; no guardian sign-up path (app.routes.ts:84). accountType=Guardian on /v1/auth/register and POST /v1/me/roles/guardian.
- apps/web/src/app/guardian-portal/guardian-register/guardian-register.ts:127: authSession.login(guardian.id) with a GUID. Removed; the flow goes through the OTP verify page.
- apps/web/src/app/app.routes.ts:83-86: public guardian register route vs an auth-required API. Now backed by the anonymous /v1/auth/register.
- apps/web/src/app/guardian-portal/guardian-link/guardian-link.ts:118: session.userId used as guardianId. The page is replaced by the candidate request page on /v1/me/guardian-links.
- apps/web/src/app/guardian-portal/guardian-link/guardian-link.ts:58,127-131: misleading 'verification link sent' message and discarded ProblemDetails. Replaced; extractErrorMessage used.
- apps/web/src/app/guardian-portal/guardian-dashboard/guardian-dashboard.ts:120,127-137,146-148: wrong guardianId, always-failing load, console.error-only errors. Rewritten on /v1/guardians/me.
- apps/web/src/app/guardian-portal/guardian-api.service.ts:15-17,35-37: calls to non-existent GET /v1/guardians/{id} and /{id}/links. Removed; the service calls only routes that exist.
- FR-45 gap: no web consent UI for guardians (give/view/withdraw) and no verification page. Added guardian-candidate-consent and guardian-verify pages.
- FR-44 gap: notice text never shown in the UI. Version label and content link shown on the candidate and guardian consent pages.
- Architecture tests: new Identity.Contracts and Guardian.Contracts plus the Consent to Guardian/Identity dependency are boundary-checked (D6).
- apps/api/tests/ExamPlatform.IntegrationTests/GuardianFlowTests.cs:18-38: duplicate guardian email accepted and only happy paths tested. Replaced by the flow and authorization tests.

## risks
- PR2 coordination: PR2 is told to add Include(CandidateLinks) to the Guardian repository and audit handlers for the current Guardian events. PR3 replaces that model (GuardianLink becomes its own aggregate root; GuardianCreatedEvent is removed), so PR2's Guardian-specific persistence and audit work is thrown away. Ask PR2 to keep its Guardian changes minimal (typed errors, conventions, MediatR removal) to limit conflicts.
- D2/D3 dependency: if PR2 names the permission differently (not 'guardian.link.manage') or puts the claims helper somewhere Consent/Guardian Endpoints cannot reference, the admin routes and ConsentActor need adjusting. The plan assumes the helper exposes GetUserId() and a permission check on 'perm' claims.
- Role claims: JwtTokenGenerator adds ClaimTypes.Role, JwtSecurityTokenHandler's outbound map writes it as 'role', and Program.cs sets MapInboundClaims=false. So ClaimsPrincipal.FindFirst(ClaimTypes.Role) and RequireRole probably never match (GetPrimaryRole returns 'Unknown'). PR3 avoids role claims and checks the Guardian role and adulthood server-side through Identity.Contracts. PR4 (role-aware web routing) and PR2 should verify the role claim type before relying on it.
- Strength of verifiable parental consent (section 7.1): an OTP to a contact the minor typed in, plus a self-declared adult DOB on the guardian account, is a weak identity signal. A minor with a second email could impersonate a guardian. Mitigations: guardian must be an adult account, a different user from the candidate, attempt and issue caps, audit trail, and the staff attestation override. Counsel must confirm this meets the DPDP Rules 2025. A stricter option is to require the guardian account's verified email/phone to equal the link's contact.
- Assumption: staff holding consent.manage cannot grant consent for minors. This deviates from the seeded description 'Record and withdraw consent on behalf of a candidate'. The supported override is staff attestation of the guardian link, after which the guardian consents. Flag it in the PR description.
- Assumption: after a candidate turns 18, consent their guardian gave while they were a minor no longer counts; the adult must re-consent. Consents recorded by a guardian whose link was later revoked stay 'active' in the ledger but are not 'effective' (IsEffective=false). They are not auto-withdrawn, which avoids a non-atomic cross-module cascade.
- All ConsentPurposes, including TermsOfService, are treated as requiring guardian consent for minors. The rule is data-driven in ConsentPurposePolicy, but the choice needs legal confirmation.
- Breaking contract changes: IConsentService.RecordConsentAsync/WithdrawConsentAsync/GetStatusAsync now take a ConsentActor, RecordConsentRequest loses GivenById, and the ConsentStatusDto/ConsentRecordDto positional records gain members. There are no consumers outside Consent yet, but M4 must use the new signatures.
- The Consent-local audit outbox overlaps with any generic outbox PR2 might build. Delivery is at-least-once, so duplicate audit rows are possible (detectable via the outboxMessageId metadata). The hosted retry service runs in every instance; with more than one API replica the relay should use SELECT ... FOR UPDATE SKIP LOCKED (noted as a follow-up). Guardian audits still use post-commit handlers (D7), so they are not atomic. Recommend generalizing the outbox to M3 later.
- The ConsentLedgerIntegrity migration rewrites data: it auto-withdraws older duplicate active rows before creating the partial unique index. Existing GivenByKind values default to 'Subject' even though pre-fix grants could have been made by anyone (IDOR). Historical ledger rows from before this PR are untrustworthy; say so in the PR description.
- The Guardian migration drops and recreates the guardian tables, losing data. Acceptable because migrations only run in Development (Program.cs) and no link could ever be verified, but anyone with a shared dev DB loses guardian rows.
- New required configuration Guardian:VerificationCodePepper, validated at startup. Non-Development environments must set it (env var Guardian__VerificationCodePepper) or the host will not start. That is intentional fail-fast, but it is an ops change.
- Rate limiting relies on PR1's named policy. If PR1 ships no such policy, fall back to a Guardian-local named fixed-window policy registered in Program.cs. The domain caps (5 attempts, 5 issues, 60 s cooldown, 3 pending links) still bound abuse.
- EF filtered unique indexes and command ordering: superseding an active consent on an older notice uses two SaveChanges inside an explicit transaction, because EF may not order UPDATE before INSERT for a filtered index. Covered by a unit test and the integration 409 test.
- Minimal API: both Identity and Guardian/Consent map groups under '/v1/me'. Route templates do not collide (profile, roles/guardian, guardian-links, exam-eligibility), but OpenAPI tags differ per module.
- The sender port is dev-only (logging). Real email/SMS delivery depends on the future Notifications module (FR-39). Until then only developers can read guardian codes, so the guardian flow is not usable in a real deployment.

## outOfScope
- Blocking invite acceptance on consent: explicitly not done. IConsentGate is for M4's POST /v1/exams/{id}/attempts, which does not exist yet.
- CSV roster guardian contacts (FR-50) and a Guardian.Contracts IGuardianLinkRequests adapter over RequestGuardianLinkHandler. PR4 adds these and calls the existing handler.
- Real email/SMS delivery of guardian codes (FR-39 Notifications module). Only the port and a dev logging adapter ship.
- Staff web UI for guardian link attestation, request and revoke. The API only; the admin UI comes with PR4 or a later admin slice.
- Role-aware nav, route guards and roles in DecodedSession (FR-2 UI). Owned by PR4; PR3 adds links from the profile and consent pages.
- Publishing new NoticeVersions, re-consent triggers on a new version, EN+HI notices, and real legal copy (FR-44 remaining gaps; section 7.3; M0 counsel deliverables).
- Proctoring-profile-driven consent purposes and camera consent for minors (FR-46, M6). ConsentRequirements.ExamAccessBaseline covers only TermsOfService and PrivacyNotice.
- Automatic withdrawal of consents when a guardian link is revoked (handled instead by the 'effective consent' rule), retention and erasure of guardian contact data (FR-47), and data-principal requests (FR-48).
- Durable, generic, multi-instance outbox and Guardian atomic audit. Consent-local outbox only.
- Fixing the Identity OTP attempt-persistence, 2FA bypass and OTP logging bugs (PR1). PR3 only avoids repeating them in Guardian.
- Question Bank (M2) and modules M4-M7.

## verification
- git checkout -b SP/feature/guardian-consent-flow <PR2 branch>; after the commits: git log --format=%B <PR2 branch>..HEAD | grep -i 'co-authored-by' must print nothing (D10), and every commit subject cites an FR/NFR ID.
- Migrations: run the three dotnet ef migrations add commands listed under migrations. Then dotnet ef migrations has-pending-model-changes --project <Infra> --startup-project apps/api/src/Host/ExamPlatform.Api --context GuardianDbContext and again with --context ConsentDbContext; both must report no pending changes. Review the SQL with dotnet ef migrations script <previous> <new> --context ... (check the partial index filters, the FK, xmin, and the dedupe SQL order).
- dotnet build apps/api/ExamPlatform.slnx -c Release (must show no new warnings about missing XML docs if docs warnings are enabled in Directory.Build.props).
- dotnet test apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests; dotnet test apps/api/tests/Modules/Consent/ExamPlatform.Modules.Consent.UnitTests; dotnet test apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests
- dotnet test apps/api/tests/ExamPlatform.ArchitectureTests
- dotnet test apps/api/tests/ExamPlatform.IntegrationTests --filter "FullyQualifiedName~GuardianConsentFlowTests|FullyQualifiedName~ConsentAuthorizationTests|FullyQualifiedName~GuardianLinkAuthorizationTests|FullyQualifiedName~AuthConsentAuditFlowTests" (Docker required for Testcontainers). Then the full dotnet test apps/api/ExamPlatform.slnx.
- Static checks: grep -rn NotImplementedException apps/api/src must be empty. grep -rn 'InvalidOperationException\|ArgumentException' apps/api/src/Modules/Guardian apps/api/src/Modules/Consent must show no throws in handlers or domain. grep -rn 'Destination' apps/api/src/Modules/Guardian/*/Events must be empty (no PII in events).
- cd apps/web && npm ci && npm run lint && npm run build && npm test -- --watch=false
- Manual smoke in Development (dotnet run --project apps/api/src/Host/ExamPlatform.Api plus npm start):
1. Register a minor (DOB 2012) and confirm that /consent hides Grant and the eligibility banner shows 'guardian required'.
2. On /me/guardian, request a link to a guardian email and check the API log: the destination must be masked, and the code appears only because Guardian:LogCodesInDevelopment=true.
3. Sign up at /guardian/register (adult DOB), verify the OTP, open /guardian/verify?linkId=<id>, enter the wrong code once and then the right one.
4. Give consent for both baseline purposes on /guardian/candidates/<id>/consent.
5. As the minor, confirm GET /v1/me/exam-eligibility returns isEligible=true.
6. As a SuperAdmin, confirm that GET /v1/admin/audit-logs shows GuardianLink.Requested, GuardianLink.Verified and Consent.Granted with no email or phone in the metadata.
- IDOR spot check with two candidate tokens: curl -H 'Authorization: Bearer <B>' '/v1/consent/status?subjectId=<A>&purpose=PrivacyNotice' must return 403 with title consent_access_denied.

