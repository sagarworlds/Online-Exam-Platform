# PR2 SP/bugfix/m3-authz-persistence
PR2 of 4: "fix: M3 authorization, persistence, typed errors and audit foundation" (FR-2, FR-40, NFR-5, NFR-6, section 11). Stacked on PR1 (SP/bugfix/identity-security). Repo root: D:\Study\Online-Exam-Platform, and all paths below are relative to it. Goal: make ExamAuthoring, Batch, Invite and Guardian correct and safe. That means per-route RBAC with the actor taken from the JWT, aggregates that load their children, typed 4xx errors instead of 500s, time from Clock, the same EF conventions M1 uses, no MediatR, audit entries for every M3 change, idempotent RBAC seeding with a documented way to seed outside Development, paging validation on the audit log, and architecture tests that find and cover every module. No new product features. The PR description lists FR IDs, and no commit carries a Co-authored-by or AI trailer (requirements section 1.1 and D10 override the harness reminder).
REQ: FR-2, FR-40, NFR-5, NFR-6, NFR-11, SEC11-TYPED-ERRORS, FR-13, FR-45, FR-50, FR-50a

## C1 chore(api): drop MediatR from M3 modules in favour of plain HandleAsync handlers (ADR 0001, FR-2)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/CreateExamCommand.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/CreateExamHandler.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/CreateBatchCommand.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/CreateBatchHandler.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/InviteCommands.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/InviteHandlers.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/GuardianCommands.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/GuardianHandlers.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringEndpoints.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchEndpoints.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteEndpoints.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianEndpoints.cs
  apps/api/src/Modules/{ExamAuthoring,Batch,Invite,Guardian}/*.Application/*.csproj (4 files)
  apps/api/src/Modules/{ExamAuthoring,Batch,Invite,Guardian}/*.Endpoints/*.csproj (4 files)
  apps/api/Directory.Packages.props
  apps/api/tests/Modules/{ExamAuthoring,Batch,Invite,Guardian}/*UnitTests/Create*HandlerTests.cs (4 files)
DETAILS: Commands become `public sealed record XCommand(...)` with no `: IRequest<T>`. Handlers become `public sealed class XHandler(...)` with no IRequestHandler, following the M1 style (AssignRoleHandler.cs), and the method is renamed `Handle` -> `HandleAsync(XCommand command, CancellationToken cancellationToken)`. Endpoints call `handler.HandleAsync`. Remove `<PackageReference Include="MediatR" />` from the 8 csproj files, and remove the `<PackageVersion Include="MediatR" .../>` line plus its '<!-- CQRS / Mediation -->' comment from Directory.Packages.props. Add full <summary>/<param>/<returns>/<exception> docstrings to every command and handler touched (section 1.3). The existing '///' one-liners are not valid XML doc. DI registrations in the *ModuleInstaller files do not change, because concrete handlers are already registered.
TESTS: Existing Create*HandlerTests compile against HandleAsync and still pass (behaviour unchanged)
  git grep -n MediatR apps/api returns nothing

## C2 refactor(api): M3 unit-of-work ports extend SharedKernel IUnitOfWork
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/IExamAuthoringUnitOfWork.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/IBatchUnitOfWork.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/IInviteUnitOfWork.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/IGuardianUnitOfWork.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/ExamAuthoringUnitOfWork.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/BatchUnitOfWork.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/InviteRepositoryAndUnitOfWork.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianRepositoryAndUnitOfWork.cs
DETAILS: Match IAdminUnitOfWork: `public interface IBatchUnitOfWork : IUnitOfWork;`. Each module keeps its own interface so DI cannot resolve another module's DbContext-backed unit of work. Implementations return `Task<int>` and become `sealed`. Delete the misleading comment in BatchUnitOfWork.cs:11-12 ('Dispatch domain events before saving'). DomainEventsSaveChangesInterceptor already dispatches after commit.
TESTS: Handler unit tests still pass (NSubstitute Received(1).SaveChangesAsync(Arg.Any<CancellationToken>()) is unchanged)

## C3 test(arch): discover modules by assembly name and enforce ADR 0001 rules for every module (NFR-11)
FILES: apps/api/tests/ExamPlatform.ArchitectureTests/ModuleCatalog.cs (new)
  apps/api/tests/ExamPlatform.ArchitectureTests/ModuleCatalogTests.cs (new)
  apps/api/tests/ExamPlatform.ArchitectureTests/DomainLayerTests.cs
  apps/api/tests/ExamPlatform.ArchitectureTests/ApplicationLayerTests.cs
  apps/api/tests/ExamPlatform.ArchitectureTests/ContractsLayerTests.cs
  apps/api/tests/ExamPlatform.ArchitectureTests/InfrastructureLayerTests.cs
  apps/api/tests/ExamPlatform.ArchitectureTests/EndpointsAndHostLayerTests.cs
  apps/api/tests/ExamPlatform.ArchitectureTests/BannedDependencyTests.cs (new)
DETAILS: ModuleCatalog loads every `ExamPlatform.Modules.*.dll` from AppContext.BaseDirectory with AssemblyLoadContext.Default.LoadFromAssemblyPath, skipping *.UnitTests. It parses `ExamPlatform.Modules.{Module}.{Layer}` into modules and their layers. Every module DLL is copied into the test output through the existing ProjectReference to ExamPlatform.Api, because Host references every Endpoints project and those reference everything below. A module the Host lists is therefore covered automatically. MemberData yields module-name strings instead of Assembly objects, so xUnit shows one test case per module. Rules replace the hard-coded {Identity, Consent, Admin} lists at ContractsLayerTests.cs:26, EndpointsAndHostLayerTests.cs:29,45, DomainLayerTests.cs:39 and InfrastructureLayerTests.cs:27. (1) Domain has no Microsoft.EntityFrameworkCore or Microsoft.AspNetCore dependency and no dependency on any other module. (2) Application does not depend on another module's .Domain./.Application./.Infrastructure./.Endpoints. layers; Contracts are allowed. (3) Admin.Application depends on no other module at all (keep the existing intent). (4) Contracts depend on no EF/AspNetCore, no other module, and not on their own Domain/Application/Infrastructure. (5) Infrastructure depends on no other module. (6) Endpoints depend on no other module except its Contracts. (7) Host depends on no module's Domain/Application/Infrastructure. Use trailing-dot prefixes such as `ExamPlatform.Modules.Batch.` so a future module whose name starts with an existing one cannot collide. BannedDependencyTests asserts that no module assembly and not the Host depends on `MediatR`. Keep the existing positive control IdentityApplication_MayDependOnAdminContracts.
TESTS: ModuleCatalogTests.DiscoversAtLeastTheKnownModules: Identity, Consent, Admin, ExamAuthoring, Batch, Invite and Guardian are all found, so the rules can never pass vacuously
  ModuleCatalogTests.EveryModule_HasDomainApplicationInfrastructureAndEndpointsLayers
  DomainLayerTests.Domain_ShouldNotDependOnFrameworks(module) / Domain_ShouldNotDependOnOtherModules(module)
  ApplicationLayerTests.Application_ShouldReachOtherModulesOnlyThroughContracts(module)
  ContractsLayerTests.Contracts_ShouldStayDependencyFree(module), for modules that have Contracts
  InfrastructureLayerTests.Infrastructure_ShouldNotDependOnOtherModules(module)
  EndpointsAndHostLayerTests.Endpoints_ShouldNotDependOnOtherModulesExceptContracts(module) and Host_ShouldOnlyReferenceModuleEndpoints
  BannedDependencyTests.NoAssembly_DependsOnMediatR(module)

## C4 feat(sharedkernel): shared claims helpers so every module reads the actor from the JWT (FR-2)
FILES: apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Security/ClaimsPrincipalExtensions.cs (new)
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Security/MissingAuthenticatedUserError.cs (new)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/ClaimsPrincipalExtensions.cs (delete)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityEndpoints.cs
  apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Endpoints/ConsentEndpoints.cs
  apps/api/tests/SharedKernel/ExamPlatform.SharedKernel.UnitTests/ExamPlatform.SharedKernel.UnitTests.csproj (new; refs SharedKernel.Domain/Application/Infrastructure, xunit, NSubstitute)
  apps/api/tests/SharedKernel/ExamPlatform.SharedKernel.UnitTests/ClaimsPrincipalExtensionsTests.cs (new)
  apps/api/ExamPlatform.slnx
DETAILS: D2 placement: the helper goes in SharedKernel.Application, which already has `<FrameworkReference Include="Microsoft.AspNetCore.App" />` and is referenced by every module's Endpoints project. That makes it usable everywhere without a new project and without an Endpoints-to-Endpoints reference, which the ADR forbids. `GetUserId(this ClaimsPrincipal)` reads the literal "sub" claim; the project should not take a JWT package dependency just for that name. It uses Guid.TryParse and throws MissingAuthenticatedUserError (DomainException, HttpStatusCode 401, ErrorCode "unauthenticated") instead of today's InvalidOperationException, NullReferenceException or FormatException, which all end up as 500s (ConsentEndpoints.cs:40 does `FindFirst("sub")!.Value`). `GetPrimaryRole` reads ClaimTypes.Role and falls back to "role", then "Unknown". Add a why-comment: JwtTokenGenerator builds `new JwtSecurityToken(claims)`, which applies no outbound claim mapping, so the long ClaimTypes.Role URI survives with MapInboundClaims=false. Reading "role" too keeps this correct if PR1 moves to SecurityTokenDescriptor. IdentityEndpoints switches to `using ExamPlatform.SharedKernel.Application.Security;` (lines 67, 73, 80). ConsentEndpoints drops its private CallerId and calls `http.User.GetUserId()`. The consent authz redesign itself is PR3.
TESTS: ClaimsPrincipalExtensionsTests.GetUserId_WithGuidSubClaim_ReturnsIt
  ClaimsPrincipalExtensionsTests.GetUserId_WithoutSubClaim_ThrowsMissingAuthenticatedUserError
  ClaimsPrincipalExtensionsTests.GetUserId_WithNonGuidSub_ThrowsMissingAuthenticatedUserError
  ClaimsPrincipalExtensionsTests.GetPrimaryRole_ReadsClaimTypesRoleOrShortRole_ElseUnknown
  AuthConsentAuditFlowTests.FullJourney still passes (Identity and Consent now use the shared helper)

## C5 test(integration): TestUsers helper that issues real sessions for seeded roles
FILES: apps/api/tests/ExamPlatform.IntegrationTests/TestUsers.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/TestJson.cs (new; shared JsonSerializerOptions with the Web defaults and JsonStringEnumConverter, lifted from AuthConsentAuditFlowTests.cs:27-30)
DETAILS: `TestUsers.CreateAsync(ApiFactory factory, string roleName)` returns `(Guid UserId, string AccessToken)`. It opens a scope, loads the role with `IdentityDbContext.Roles.Include(r => r.Permissions).SingleAsync(r => r.Name == roleName)`, calls `User.Register($"{roleName}-{Guid.NewGuid():N}@example.test", null, new DateOnly(1990,1,1), roleName, clock.UtcNow)`, then AssignRole and Activate, adds the user, calls the DI-resolved `LoginSessionIssuer.Issue(user, null, null)`, and saves. `CreateClientAsync(factory, roleName)` returns an HttpClient with the Bearer header already set. Why: the tokens carry the real "perm" claims produced by the seeded grants and a persisted sid. Tests therefore exercise production RBAC and keep working if PR1 validates sid, which would reject TestJwtTokenBuilder's synthetic tokens (they carry no perm or sid).
TESTS: Used by every M3 integration test from here on

## C6 fix(api): take exam/batch/invite creator from the token, not the request body (FR-2, NFR-5)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringEndpoints.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchEndpoints.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteEndpoints.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Dtos/CreateExamRequest.cs (delete: dead duplicate)
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Dtos/BatchDto.cs (remove dead CreateBatchRequest)
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Dtos/InviteDto.cs (remove dead CreateInviteRequest/GenerateInviteCodeRequest/AcceptInviteRequest)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Dtos/GuardianDto.cs (remove dead CreateGuardianRequest/LinkCandidateRequest/VerifyGuardianLinkRequest)
  apps/api/tests/ExamPlatform.IntegrationTests/ExamAuthoringFlowTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/BatchFlowTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/InviteFlowTests.cs
DETAILS: Remove `Guid CreatedBy` from CreateExamRequest (ExamAuthoringEndpoints.cs:39-43) and CreateBatchRequest (BatchEndpoints.cs:87-92), and `Guid CreatedByUserId` from CreateInviteRequest (InviteEndpoints.cs:103-107). Each handler lambda gains a `ClaimsPrincipal user` parameter, which minimal APIs bind automatically, and builds the command with `user.GetUserId()` (lines 31, 44, 47). The Endpoints request records stay the only request contract. The unused Application-layer duplicates had different shapes and are deleted. System.Text.Json ignores unknown members by default, so an old client that still sends createdBy is ignored, not rejected. Integration tests switch to TestUsers with the SuperAdmin role for now; per-role clients follow in the authorization commit.
TESTS: ExamAuthoringFlowTests.CreateExam_WithSpoofedCreatedByInBody_AttributesCreatorFromTokenSubject: post raw JSON that includes createdBy=random; response createdBy equals the TestUsers user id
  BatchFlowTests.CreateBatch_WithSpoofedCreatedByInBody_AttributesCreatorFromTokenSubject
  InviteFlowTests.CreateInvite_WithSpoofedCreatedByUserId_IsIgnored (asserted through InviteDbContext CreatedByUserId)

## C7 fix(web): stop sending createdBy/createdByUserId; the API takes the creator from the token (FR-2)
FILES: apps/web/src/app/exam-authoring/exam-api.service.ts
  apps/web/src/app/exam-authoring/exam-api.service.spec.ts
  apps/web/src/app/exam-authoring/exam-builder/exam-builder.ts
  apps/web/src/app/batch-management/batch-api.service.ts
  apps/web/src/app/batch-management/batch-api.service.spec.ts
  apps/web/src/app/batch-management/batch-create/batch-create.ts
  apps/web/src/app/invite-management/invite-api.service.ts
  apps/web/src/app/invite-management/invite-api.service.spec.ts
  apps/web/src/app/invite-management/invite-create/invite-create.ts
DETAILS: createExam(request), createBatch(request) and createInvite(request) lose the userId parameter and post the request unchanged. The components stop passing session.userId. The `if (!session?.userId)` guards in exam-builder.ts:151, batch-create.ts:125 and invite-create.ts:112 are removed; authGuard already protects the routes and the API returns 401 or 403. Specs change from `expect(req.request.body.createdBy).toBe(userId)` (exam-api.service.spec.ts:33) to asserting the field is absent.
TESTS: exam-api.service.spec 'should create an exam without a createdBy field'
  batch-api.service.spec 'should create a batch without a createdBy field'
  invite-api.service.spec 'should create an invite without a createdByUserId field'

## C8 fix(identity): idempotent RBAC upsert seeder with exam, batch, invite and guardian permissions (FR-2)
FILES: apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Domain/Rbac/RbacCatalog.cs (new)
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/IdentitySeeder.cs
  apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/RbacCatalogTests.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/RbacSeedingTests.cs (new)
DETAILS: RbacCatalog is pure, dependency-free reference data: `PermissionDefinition(Code, Description)` and `RoleDefinition(Name, RequiresTwoFactor, IReadOnlyList<string> PermissionCodes)`. Permissions: admin.audit.read, consent.manage, identity.role.assign, exam.manage, exam.publish, batch.manage, batch.read, invite.manage, guardian.link.manage. Grants (D3): SuperAdmin gets all. ExamAdmin gets exam.manage, exam.publish, batch.manage, batch.read, invite.manage, guardian.link.manage. InstituteTeacher gets batch.manage, batch.read, invite.manage. ContentAuthor, Reviewer, Proctor, Candidate and Guardian get none. The eight role names and 2FA flags stay exactly as IdentitySeeder.cs:31-42 has them. SeedAsync replaces the early return at IdentitySeeder.cs:19-22 with this sequence: load existing Permissions into a dictionary by Code and add missing ones; load Roles with `.Include(r => r.Permissions)` into a dictionary by Name and create missing ones; call the already-idempotent `role.Grant(permission)` for every catalog grant; SaveChangesAsync once. Why-comments to add: (a) the seeder is additive only. It never revokes grants or flips RequiresTwoFactor on an existing role, because taking a capability away should be a deliberate, audited admin action, not a side effect of startup. (b) Permissions are baked into the JWT at login (JwtTokenGenerator), so users pick up new grants at their next login.
TESTS: RbacCatalogTests.SuperAdmin_HoldsEveryDefinedPermission
  RbacCatalogTests.EveryRoleGrant_ReferencesADefinedPermissionCode
  RbacCatalogTests.PermissionCodes_AndRoleNames_AreUnique
  RbacCatalogTests.ExamAdmin_AndInstituteTeacher_Grants_MatchPermissionMatrix
  RbacCatalogTests.Candidate_AndGuardian_HaveNoPermissions
  RbacSeedingTests.SeedAsync_OnDatabaseSeededByOldSeeder_AddsMissingPermissionsAndGrants (delete exam.manage and its RolePermissions rows with SQL, rerun IdentitySeeder.SeedAsync, assert it is back and granted to ExamAdmin)
  RbacSeedingTests.SeedAsync_RunTwice_DoesNotDuplicateRolesPermissionsOrGrants

## C9 feat(host): run idempotent migrate-and-seed outside Development via --migrate-and-seed (FR-2, ADR 0002)
FILES: apps/api/src/Host/ExamPlatform.Api/Program.cs
  apps/api/src/Host/ExamPlatform.Api/appsettings.json
  apps/api/src/Host/ExamPlatform.Api/appsettings.Development.json
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/IModuleInstaller.cs
  docs/adr/0002-migrations-and-reference-data-seeding.md (new, from docs/adr/template.md)
  README.md
  apps/api/tests/ExamPlatform.IntegrationTests/NonDevelopmentStartupTests.cs (new)
DETAILS: Decision, recorded in ADR 0002: reference data such as roles, permissions and notice versions is seeded by each module's idempotent `IModuleInstaller.MigrateAndSeedAsync`, never by EF migration bundles. Bundles apply schema only and would skip the seeders, leaving a non-dev environment where registration fails with RoleNotFoundError. Program.cs replaces the IsDevelopment() check at line 126 with `var migrateAndSeedOnly = args.Contains("--migrate-and-seed", StringComparer.Ordinal); var migrateOnStartup = app.Configuration.GetValue("Database:MigrateAndSeedOnStartup", app.Environment.IsDevelopment());`. If either is true it runs every module installer and logs `Migrated and seeded {ModuleName}`. If migrateAndSeedOnly is set it returns before app.Run(). Deployment runs `dotnet ExamPlatform.Api.dll --migrate-and-seed` once as a pre-deploy job or init container, then starts the web replicas with the flag false. A single runner avoids races on the unique indexes of Roles.Name and Permissions.Code. Config values: false in appsettings.json, true in appsettings.Development.json. Update IModuleInstaller.cs:27-35 so the doc no longer says 'Development only', and correct the Program.cs:121-123 comment and README 'Database Management'. The ADR also records a known leftover: the M3 contexts have no HasDefaultSchema, so they share public.__EFMigrationsHistory. That is left alone on purpose, because moving the history table would re-run InitialCreate against existing databases. ApiFactory stays on Development.
TESTS: NonDevelopmentStartupTests.ProductionEnvironment_WithMigrateAndSeedFlag_SeedsRolesPermissionsAndNotices (a WebApplicationFactory subclass using UseEnvironment("Production"), Database:MigrateAndSeedOnStartup=true and its own Postgres container; asserts 9 permissions, 8 roles and 3 notice versions)

## C10 fix(api): enforce per-route permission policies on exam, batch, invite and guardian endpoints (FR-2, NFR-5)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringPermissions.cs (new)
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringEndpoints.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchPermissions.cs (new)
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchEndpoints.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InvitePermissions.cs (new)
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteEndpoints.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianPermissions.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianEndpoints.cs
  apps/api/tests/ExamPlatform.IntegrationTests/M3AuthorizationTests.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/RbacSeedingTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/BatchFlowTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/InviteFlowTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/ExamAuthoringFlowTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/GuardianFlowTests.cs
DETAILS: Each module gets policy-name constants, for example `public const string Manage = "permission:batch.manage";`. They resolve through the existing PermissionPolicyProvider, which Identity registers once. Groups keep `.RequireAuthorization()` for the authenticated baseline (ExamAuthoringEndpoints.cs:15, BatchEndpoints.cs:15, InviteEndpoints.cs:15, GuardianEndpoints.cs:15), and each route adds its own `.RequireAuthorization(X.Manage)`. POST /v1/exams requires exam.manage (exam.publish is seeded for PR4's publish endpoint). The four /v1/batches routes require batch.manage (batch.read is seeded for PR4's GETs). The /v1/invites routes create, {id}/codes and {id}/revoke require invite.manage. {id}/accept and {id}/decline also require invite.manage for now, with this why-comment: once the Include fix lands, accept-by-GUID with no caller binding would let any user consume someone else's single-use invite. PR4 replaces these with an owner-bound POST /v1/invites/{code}/accept. Every /v1/guardians route requires guardian.link.manage, with a why-comment that these are staff-only until PR3 adds guardian accounts and self-service. Add `.ProducesProblem(401/403)` metadata. The existing M3 flow tests switch to role-appropriate TestUsers clients, and GuardianFlowTests switches from a Guardian token to ExamAdmin.
TESTS: M3AuthorizationTests.Candidate_CreateExam_Returns403
  M3AuthorizationTests.Candidate_CreateBatch_Returns403 / Candidate_ActivateBatch_Returns403
  M3AuthorizationTests.Candidate_CreateInvite_Returns403 / Candidate_GenerateInviteCode_Returns403 / Candidate_RevokeInvite_Returns403
  M3AuthorizationTests.Guardian_CreateGuardianRecord_Returns403
  M3AuthorizationTests.InstituteTeacher_CreateBatch_Returns201 / InstituteTeacher_CreateInvite_Returns201 / InstituteTeacher_CreateExam_Returns403
  M3AuthorizationTests.ExamAdmin_CreateExam_Returns201 / ExamAdmin_CreateGuardian_Returns201
  RbacSeedingTests.EveryPermissionPolicyUsedByAnEndpoint_IsSeeded: resolve EndpointDataSource, collect IAuthorizeData.Policy values that start with 'permission:', and assert each code exists in identity.Permissions, so a typo in a policy name fails CI
  Existing *_WithoutAuth_ReturnsUnauthorized tests still return 401

## C11 fix(exam-authoring): typed domain errors for not-found, invalid state and validation (section 11)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exceptions/ExamNotDraftError.cs (new, 409 exam_not_draft)
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exceptions/InvalidExamDetailsError.cs (new, 400 invalid_exam)
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exceptions/{ExamNotFoundError,InvalidExamConfigError,DuplicateQuestionError}.cs (docstrings)
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exam.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/CreateExamHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Ports/IExamRepository.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/Repositories/EFExamRepository.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/ExamTests.cs (new)
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/CreateExamHandlerTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/ExamAuthoringFlowTests.cs
DETAILS: Exam.Publish throws ExamNotDraftError instead of InvalidOperationException (Exam.cs:76). Blank-name validation moves from CreateExamHandler.cs:14 (ArgumentException) into the Exam constructor as InvalidExamDetailsError, so the invariant lives on the aggregate. Following the Identity pattern (AssignRoleHandler: `?? throw new UserNotFoundError()`), remove GetByIdOrThrowAsync from the IExamRepository port and from EFExamRepository.cs:17-22, where it throws InvalidOperationException. Future handlers write `await repo.GetByIdAsync(id, ct) ?? throw new ExamNotFoundError(id)`, which keeps the throw unit-testable. Delete the dangling '/// Update an existing exam aggregate.' comment at IExamRepository.cs:20.
TESTS: ExamTests.Create_WithBlankName_ThrowsInvalidExamDetailsError
  ExamTests.Publish_WhenAlreadyPublished_ThrowsExamNotDraftError
  ExamTests.Publish_WithoutSections_ThrowsInvalidExamConfigError
  CreateExamHandlerTests.HandleAsync_WithBlankName_ThrowsInvalidExamDetailsError (replaces the ArgumentException expectation)
  ExamAuthoringFlowTests.CreateExam_WithBlankName_Returns400ProblemDetailsWithTitleInvalidExam

## C12 fix(batch): typed domain errors, member input validation and a PII-free duplicate message (section 11, NFR-6)
FILES: apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Exceptions/DuplicateMemberError.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Exceptions/InvalidBatchMemberError.cs (new, 400 invalid_batch_member)
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Batch.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/CreateBatchHandler.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Ports/IBatchRepository.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/Repositories/EFBatchRepository.cs
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/BatchTests.cs (new)
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/CreateBatchHandlerTests.cs
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/BatchLifecycleHandlerTests.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/BatchFlowTests.cs
DETAILS: The Batch constructor validates maxMembers > 0 and a non-blank name, throwing InvalidBatchConfigError. This replaces the ArgumentException at CreateBatchHandler.cs:14. AddMember trims and lower-cases the email. The why-comment: emails are case-insensitive identifiers, so without normalization 'A@x' and 'a@x' would create two seats for one person. Email is checked with `MailAddress.TryCreate` (no exception-driven control flow) and phone with `^\+?[1-9]\d{1,14}$` plus max length 20. Invalid input throws InvalidBatchMemberError; today it surfaces as DbUpdateException 500 when Phone exceeds the HasMaxLength(20) column. DuplicateMemberError drops the email from its message (DuplicateMemberError.cs:5), because the message is logged by DomainExceptionHandler and returned in ProblemDetails.Detail (NFR-6). Remove GetByIdOrThrowAsync from the port and from EFBatchRepository.cs:24-30. AddBatchMember, Activate and Close handlers use `?? throw new BatchNotFoundError(command.BatchId)`.
TESTS: BatchTests.Create_WithZeroMaxMembers_ThrowsInvalidBatchConfigError
  BatchTests.AddMember_InvalidEmail_ThrowsInvalidBatchMemberError / AddMember_PhoneTooLong_ThrowsInvalidBatchMemberError
  BatchTests.AddMember_SameEmailDifferentCase_ThrowsDuplicateMemberError
  BatchTests.AddMember_AtCapacity_ThrowsInvalidBatchConfigError
  BatchTests.Close_WhenPending_ThrowsInvalidBatchConfigError
  BatchTests.DuplicateMemberError_Message_DoesNotContainTheEmail
  CreateBatchHandlerTests.HandleAsync_WithZeroMaxMembers_ThrowsInvalidBatchConfigError
  BatchLifecycleHandlerTests.Activate_UnknownBatch_ThrowsBatchNotFoundError (repository substitute returns null)
  BatchFlowTests.ActivateBatch_UnknownId_Returns404WithTitleBatchNotFound (was 500)
  BatchFlowTests.AddMember_PhoneOver20Chars_Returns400 (was 500)

## C13 fix(invite): typed errors for invite state transitions, expiry bounds and validation (section 11, FR-50a)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Exceptions/InviteNotPendingError.cs (new, 409 invite_not_pending)
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Exceptions/InviteNotRevocableError.cs (new, 409 invite_not_revocable)
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Exceptions/InvalidInviteExpiryError.cs (new, 400 invalid_invite_expiry)
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Exceptions/InvalidInviteDetailsError.cs (new, 400 invalid_invite)
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/InviteHandlers.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Ports/IInviteRepository.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/InviteRepositoryAndUnitOfWork.cs
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/InviteTests.cs (new)
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/CreateInviteHandlerTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/InviteFlowTests.cs
DETAILS: Invite.cs:68 and :85 throw InviteNotPendingError, and :97 throws InviteNotRevocableError. Email validation moves from InviteHandlers.cs:14-16 (ArgumentException) into the Invite constructor using MailAddress.TryCreate and throws InvalidInviteDetailsError. GenerateCode enforces 1 <= expiryHours <= 720 and throws InvalidInviteExpiryError. The why-comment: an invite code is a bearer secret, so a long-lived code widens the window for leaks and brute force, while 0, negative or huge values used to create already-expired codes or throw ArgumentOutOfRangeException from DateTime.AddHours as a 500. Remove GetByIdOrThrowAsync (InviteRepositoryAndUnitOfWork.cs:15-20); handlers use `?? throw new InviteNotFoundError(id)`. InvalidInviteCodeError stays as the error for an unknown or invalid code; PR4 may add InviteExpiredError when it builds accept-by-code.
TESTS: InviteTests.Accept_WhenDeclined_ThrowsInviteNotPendingError
  InviteTests.Decline_WhenAccepted_ThrowsInviteNotPendingError
  InviteTests.Revoke_WhenAlreadyRevoked_ThrowsInviteNotRevocableError
  InviteTests.GenerateCode_WithZeroOrNegativeHours_ThrowsInvalidInviteExpiryError / GenerateCode_Over720Hours_ThrowsInvalidInviteExpiryError
  InviteTests.Create_WithInvalidEmail_ThrowsInvalidInviteDetailsError
  CreateInviteHandlerTests.HandleAsync_WithInvalidEmail_ThrowsInvalidInviteDetailsError
  InviteFlowTests.GenerateCode_UnknownInvite_Returns404WithTitleInviteNotFound (was 500)
  InviteFlowTests.GenerateCode_WithExpiryHours100000_Returns400 (was 500)

## C14 fix(guardian): typed errors, no silent no-op revoke/unlink, drop the unimplemented verify route (section 1.2, FR-45)
FILES: apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Exceptions/GuardianAlreadyLinkedError.cs (new, 409 guardian_already_linked)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Exceptions/GuardianLinkNotFoundError.cs (new, 404 guardian_link_not_found)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Exceptions/GuardianLinkNotPendingError.cs (new, 409 guardian_link_not_pending)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Exceptions/GuardianLinkAlreadyRevokedError.cs (new, 409 guardian_link_already_revoked)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Exceptions/InvalidGuardianDetailsError.cs (new, 400 invalid_guardian)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Guardian.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/GuardianLink.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/GuardianCommands.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/GuardianHandlers.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Ports/IGuardianRepository.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianRepositoryAndUnitOfWork.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianEndpoints.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianModuleInstaller.cs
  apps/web/src/app/guardian-portal/guardian-api.service.ts
  apps/web/src/app/guardian-portal/guardian.models.ts
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/GuardianTests.cs (new)
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/CreateGuardianHandlerTests.cs
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/GuardianLinkHandlerTests.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/GuardianFlowTests.cs
DETAILS: Guardian.cs:39 throws GuardianAlreadyLinkedError. GuardianLink.cs:34 throws GuardianLinkNotPendingError and :44 throws GuardianLinkAlreadyRevokedError. Add the aggregate method `Guardian.RevokeCandidateLink(candidateId)`, and make `UnlinkCandidate` throw GuardianLinkNotFoundError when no active link exists. This replaces the silent `if (link != null)` no-ops that returned 204 (GuardianHandlers.cs:72-77, 86-88), which section 1.2 forbids. Email and name validation moves into the Guardian constructor as InvalidGuardianDetailsError (GuardianHandlers.cs:16,19). Delete VerifyGuardianLinkCommand, VerifyGuardianLinkHandler (the NotImplementedException at GuardianHandlers.cs:62), the POST /v1/guardians/links/verify route and request record, and its DI registration. Web: remove `verifyLink` and `VerifyGuardianLinkRequest`; no component calls them. InvalidLinkTokenError is kept for PR3's OTP-based verification. Remove GetByIdOrThrowAsync (GuardianRepositoryAndUnitOfWork.cs:15-20) and use `?? throw new GuardianNotFoundError(id)` instead.
TESTS: GuardianTests.LinkCandidate_WhenAlreadyLinked_ThrowsGuardianAlreadyLinkedError
  GuardianTests.RevokeCandidateLink_WithNoLink_ThrowsGuardianLinkNotFoundError
  GuardianTests.RevokeCandidateLink_Twice_ThrowsGuardianLinkAlreadyRevokedError
  GuardianTests.UnlinkCandidate_WithNoLink_ThrowsGuardianLinkNotFoundError
  GuardianLinkHandlerTests.Revoke_UnknownGuardian_ThrowsGuardianNotFoundError
  CreateGuardianHandlerTests.HandleAsync_WithInvalidEmail_ThrowsInvalidGuardianDetailsError
  GuardianFlowTests.RevokeLink_UnknownGuardian_Returns404 (was 500)
  GuardianFlowTests.VerifyLinkRoute_IsNoLongerMapped_Returns404 (was 500 NotImplementedException)

## C15 fix(persistence): load aggregate child collections in M3 repositories (FR-50, FR-50a, FR-45)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/Repositories/EFExamRepository.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/Repositories/EFBatchRepository.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/InviteRepositoryAndUnitOfWork.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianRepositoryAndUnitOfWork.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/ExamAuthoringDbContext.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/BatchDbContext.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/InviteDbContext.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianDbContext.cs
  apps/api/tests/ExamPlatform.IntegrationTests/BatchFlowTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/InviteFlowTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/GuardianFlowTests.cs
DETAILS: Follow UserRepository.cs:13-15 by giving each repository a private `Loaded()` query. Exams: `.Include(e => e.Sections).ThenInclude(s => s.Questions).AsSplitQuery()`. Batches: `.Include(b => b.Members)`. Invites: `.Include(i => i.Codes)`. Guardians: `.Include(g => g.CandidateLinks)`. GetByIdAsync keeps tracking, as the existing comment explains. The ListBy* and GetByEmailAsync methods use `Loaded().AsNoTracking()` so read DTOs such as ActiveMemberCount are correct. Add a why-comment that without the Include every domain invariant sees an empty collection (the da97c5b follow-up). In each DbContext, make the backing field explicit, as IdentityDbContext.cs:49,71 does: `b.Navigation(x => x.Members).HasField("_members").UsePropertyAccessMode(PropertyAccessMode.Field)`, plus Sections/_sections, Questions/_questions, Codes/_codes and CandidateLinks/_candidateLinks. There is no schema change; confirm with has-pending-model-changes.
TESTS: BatchFlowTests.ActivateBatch_AfterMemberAddedInEarlierRequest_Returns204 (was 400: batch appeared to have no members)
  BatchFlowTests.CloseBatch_AfterActivate_Returns204 (was unreachable)
  BatchFlowTests.AddMember_SameEmailInSeparateRequests_Returns409DuplicateMember (the duplicate used to be inserted)
  BatchFlowTests.AddMember_BeyondCapacityInSeparateRequests_Returns400
  InviteFlowTests.AcceptInvite_WithCodeGeneratedInEarlierRequest_Returns204 (was always 400)
  InviteFlowTests.AcceptInvite_Twice_Returns409InviteNotPending
  InviteFlowTests.RevokeInvite_RevokesCodesPersistedEarlier (InviteDbContext shows RevokedAt set)
  GuardianFlowTests.LinkCandidate_SameCandidateTwice_Returns409 (duplicate link used to be created)
  GuardianFlowTests.RevokeLink_ThenRevokeAgain_Returns409 (was a silent 204)

## C16 fix(invite): drop the unused DateTime Invite.CreatedBy column (data model)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/Migrations/<ts>_DropInviteCreatedBy.cs (new)
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/Migrations/<ts>_DropInviteCreatedBy.Designer.cs (new)
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/Migrations/InviteDbContextModelSnapshot.cs
DETAILS: Remove `public DateTime CreatedBy` (Invite.cs:18). It was a copy-paste of CreatedByUserId, never assigned, and persisted as NOT NULL -infinity. This must land before the UTC convention commit, because RequireUtc would reject its Kind=Unspecified DateTime.MinValue on every insert. Generate with: `dotnet ef migrations add DropInviteCreatedBy --project src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure --startup-project src/Host/ExamPlatform.Api --context InviteDbContext --output-dir Migrations`. Up drops the column. Down re-adds it as timestamptz NOT NULL with default '-infinity'.
TESTS: InviteFlowTests.CreateInvite_* still pass
  has-pending-model-changes reports none for InviteDbContext

## C17 fix(persistence): apply UTC and client-generated-key conventions to M3 DbContexts; make exam schedule optional (D5, FR-13)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/ExamAuthoringDbContext.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/BatchDbContext.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/InviteDbContext.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianDbContext.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exam.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Events/ExamPublishedEvent.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/CreateExamHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Dtos/ExamDto.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/Migrations/<ts>_MakeExamScheduleOptional.cs (+ .Designer.cs, snapshot)
  apps/web/src/app/exam-authoring/exam.models.ts
  apps/api/tests/ExamPlatform.IntegrationTests/ExamAuthoringFlowTests.cs
DETAILS: At the end of each M3 OnModelCreating, call `modelBuilder.ApplyUtcDateTimeConversion(); modelBuilder.ApplyClientGeneratedGuidKeys();` (ExamAuthoringDbContext.cs:12 and the Batch/Invite/Guardian contexts), matching IdentityDbContext.cs:102-103. Also add `b.Ignore(x => x.DomainEvents)` on the aggregates for parity with Identity and Consent. Exam.ScheduledStartTime and ScheduledEndTime become `DateTime?`, and the constructor drops its schedule parameters. CreateExamHandler.cs:20-21 stops writing DateTime.MinValue: that value is Kind=Unspecified, which the UTC converter would reject, and it made unscheduled exams look scheduled at -infinity. ExamPublishedEvent.ScheduledStartTime and ExamDto become nullable. The migration uses AlterColumn nullable for both columns, then runs `migrationBuilder.Sql("UPDATE \"examAuthoring\".\"Exams\" SET \"ScheduledStartTime\" = NULL WHERE \"ScheduledStartTime\" = '-infinity'; UPDATE ... \"ScheduledEndTime\" ...")`. Down reverses it by filling NULLs with '-infinity' before setting NOT NULL. Web: `scheduledStartTime: Date | null; scheduledEndTime: Date | null;`. For Batch, Invite and Guardian, has-pending-model-changes should report nothing, because the converters are DateTime-to-DateTime and do not change column types. Do not commit empty migrations.
TESTS: ExamTests.Create_LeavesScheduleUnset
  ExamAuthoringFlowTests.CreateExam_ReturnsNullScheduleAndUtcTimestamps (createdAt parses as UTC; scheduledStartTime is null)
  Full M3 integration suite still green, which shows no non-UTC DateTime reaches Npgsql

## C18 fix(invite): optimistic concurrency on Invite so a single-use accept cannot double-commit (FR-50a)
FILES: apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Domain/ConcurrencyConflictError.cs (new, 409 concurrency_conflict)
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/InviteDbContext.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/InviteRepositoryAndUnitOfWork.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/Migrations/<ts>_AddInviteRowVersion.cs (+ .Designer.cs, snapshot)
  apps/api/tests/ExamPlatform.IntegrationTests/InviteFlowTests.cs
DETAILS: Map Postgres xmin as a shadow concurrency token: `i.Property<uint>("Version").IsRowVersion();`. Npgsql maps a uint row version to the xmin system column, so the Domain gains no persistence field. Every Accept, Decline, Revoke or GenerateCode updates the Invite row (Status/UpdatedAt), so the token covers the code's single use. InviteUnitOfWork catches DbUpdateConcurrencyException, logs the entity type and id (never the email), and rethrows ConcurrencyConflictError. That exception lives in SharedKernel.Domain so later modules can reuse it (OCP). Review the scaffolded migration: if EF emitted AddColumn "xmin", delete that operation from Up and Down by hand, because xmin always exists, and keep the snapshot.
TESTS: InviteFlowTests.AcceptInvite_TwoConcurrentRequests_ExactlyOneSucceeds (Task.WhenAll of two accepts: one 204, the other 409 with invite_not_pending or concurrency_conflict)

## C19 fix(persistence): unique active-member and active-link indexes backing batch/guardian invariants (FR-50, FR-45)
FILES: apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/BatchDbContext.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/BatchUnitOfWork.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/Migrations/<ts>_UniqueActiveBatchMemberEmail.cs (+ .Designer.cs, snapshot)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianDbContext.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/GuardianRepositoryAndUnitOfWork.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Infrastructure/Migrations/<ts>_UniqueActiveGuardianLink.cs (+ .Designer.cs, snapshot)
  apps/api/tests/ExamPlatform.IntegrationTests/BatchFlowTests.cs
DETAILS: Add `bm.HasIndex(x => new { x.BatchId, x.Email }).IsUnique().HasFilter("\"IsDeleted\" = false")` and `l.HasIndex(x => new { x.GuardianId, x.CandidateId }).IsUnique().HasFilter("\"IsDeleted\" = false")`. Each migration runs SQL before creating its index. Batch: lower(trim(Email)), then soft-delete any active duplicate, keeping the earliest CreatedAt and breaking ties on Id. Guardian: soft-delete active duplicate links the same way. Existing dev databases may already hold duplicates from the missing-Include bug, and CREATE UNIQUE INDEX would fail on them. BatchUnitOfWork and GuardianUnitOfWork catch DbUpdateException whose inner PostgresException has SqlState 23505 on that index name and rethrow DuplicateMemberError or GuardianAlreadyLinkedError, so a race still returns a typed 409. Why-comment: the domain check handles the common case and the index handles concurrent requests.
TESTS: BatchFlowTests.AddMember_SameEmailConcurrently_OneSucceedsOtherReturns409
  GuardianFlowTests.LinkCandidate_SameCandidateConcurrently_OneSucceedsOtherReturns409

## C20 refactor(exam-authoring): drive time from Clock, encapsulate aggregate state, carry actor on events (D5, FR-40)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exam.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/ExamSection.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/ExamQuestion.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Events/ExamCreatedEvent.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Events/ExamPublishedEvent.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/CreateExamHandler.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/FakeClock.cs (new, copy of the Consent FakeClock)
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/ExamTests.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/CreateExamHandlerTests.cs
DETAILS: Use `public static Exam Create(Guid? seriesId, string name, string? description, Guid createdBy, DateTime nowUtc)`. UpdateConfig(config, nowUtc), AddSection(name, timeSeconds, nowUtc) and Publish(actorUserId, nowUtc) replace every DateTime.UtcNow (Exam.cs:41-82, ExamSection.cs:26, ExamQuestion.cs:19). Setters become private (Exam.cs:11-25 and the child entities), with EF using backing fields. Remove `public new Guid Id => base.Id;` (Exam.cs:10) so EF maps Entity.Id's protected init like Identity does. Events become positional records `: IDomainEvent` with an explicit OccurredAtUtc, as UserRegisteredEvent does: `ExamCreatedEvent(Guid ExamId, Guid CreatedBy, DateTime OccurredAtUtc)` (the name is dropped from the event because audit metadata only needs ids) and `ExamPublishedEvent(Guid ExamId, DateTime? ScheduledStartTime, Guid ActorUserId, DateTime OccurredAtUtc)`. CreateExamHandler injects Clock.
TESTS: ExamTests.Create_StampsCreatedAtAndEventTimeFromSuppliedNow
  ExamTests.AddSection_NumbersSectionsSequentially
  ExamTests.Publish_RaisesExamPublishedEventWithActor
  CreateExamHandlerTests updated to FakeClock and asserting CreatedAt == clock.UtcNow

## C21 refactor(batch): drive time from Clock, encapsulate aggregate state, carry actor on events (D5, FR-40)
FILES: apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Batch.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/BatchMember.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Events/BatchCreatedEvent.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Events/BatchActivatedEvent.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Events/BatchClosedEvent.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Events/BatchMemberAddedEvent.cs (new)
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/CreateBatchCommand.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/CreateBatchHandler.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchEndpoints.cs
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/FakeClock.cs (new)
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/BatchTests.cs
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/CreateBatchHandlerTests.cs
DETAILS: Use `Batch.Create(examId, name, description, maxMembers, createdBy, nowUtc)`, `AddMember(email, phone, actorUserId, nowUtc)`, which raises `BatchMemberAddedEvent(BatchId, MemberId, ActorUserId, OccurredAtUtc)`, `Activate(actorUserId, nowUtc)`, `Close(actorUserId, nowUtc)` and `RemoveMember(memberId, nowUtc)`. BatchMember methods take nowUtc. This replaces DateTime.UtcNow at Batch.cs:34,35,50,59,80,90,97 and BatchMember.cs:25-52. Setters become private and `public new Guid Id` is removed (Batch.cs:10). AddBatchMemberCommand, ActivateBatchCommand and CloseBatchCommand gain `Guid ActorUserId`, which the endpoints fill from `user.GetUserId()`. Events become positional records `: IDomainEvent` with ActorUserId and OccurredAtUtc; BatchCreatedEvent drops Name.
TESTS: BatchTests.Activate_WithMember_SetsActiveAndRaisesEventWithActorAndClockTime
  BatchTests.AddMember_RaisesBatchMemberAddedEvent
  CreateBatchHandlerTests use FakeClock

## C22 refactor(invite): drive time from Clock, CSPRNG codes, encapsulate state, carry actor on events (D5, FR-50a, FR-40)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/InviteCode.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Events/InviteCreatedEvent.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Events/InviteAcceptedEvent.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Events/InviteDeclinedEvent.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Events/InviteRevokedEvent.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Events/InviteCodeIssuedEvent.cs (new)
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/InviteCommands.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/InviteHandlers.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteEndpoints.cs
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/FakeClock.cs (new)
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/InviteTests.cs
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/CreateInviteHandlerTests.cs
DETAILS: Use `Invite.Create(examId, batchMemberId, email, createdByUserId, nowUtc)`, `GenerateCode(expiryHours, actorUserId, nowUtc)` (raises InviteCodeIssuedEvent(InviteId, CodeId, ExpiresAt, ActorUserId, OccurredAtUtc) and never includes the code value, which is a bearer secret), `Accept(codeId, actorUserId, nowUtc)`, `Decline(actorUserId, nowUtc)` and `Revoke(actorUserId, nowUtc)`. InviteCode.IsValid(nowUtc), IsExpired(nowUtc), MarkAsUsed(nowUtc) and Revoke(nowUtc) replace DateTime.UtcNow at InviteCode.cs:21-45 and Invite.cs:36-109. Why-comment on the expiry boundary: a code is valid up to and including ExpiresAt. GenerateUniqueCode uses `RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 8)` instead of `new Random()` (Invite.cs:115), because a guessable code defeats single-use. The unique index, hashing and lookup by code are PR4. Setters become private and `public new Guid Id` (Invite.cs:10) is removed. The GenerateCode, Accept, Decline and Revoke commands gain ActorUserId. Events keep Email because PR4's enrollment handler may need it, but the audit handlers must never persist it.
TESTS: InviteTests.Accept_AfterExpiry_ThrowsInvalidInviteCodeError (FakeClock moved past ExpiresAt, deterministic)
  InviteTests.Accept_AtExactExpiry_Succeeds
  InviteTests.Revoke_RevokesAllOutstandingCodes_WithClockTime
  InviteTests.GenerateCode_ProducesEightCharsFromAlphabet
  InviteTests.GenerateCode_RaisesInviteCodeIssuedEventWithoutCodeValue

## C23 refactor(guardian): drive time from Clock, encapsulate state, raise link events with actor (D5, FR-45, FR-40)
FILES: apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Guardian.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/GuardianLink.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Events/GuardianCreatedEvent.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Events/GuardianLinkRevokedEvent.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Events/GuardianLinkVerifiedEvent.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Events/GuardianLinkRequestedEvent.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Events/GuardianCandidateUnlinkedEvent.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/GuardianCommands.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Commands/GuardianHandlers.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianEndpoints.cs
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/FakeClock.cs (new)
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/GuardianTests.cs
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/CreateGuardianHandlerTests.cs
DETAILS: Use `Guardian.Create(email, fullName, phone, createdByUserId, nowUtc)`, `LinkCandidate(candidateId, candidateEmail, verificationToken, actorUserId, nowUtc)` (raises GuardianLinkRequestedEvent), `RevokeCandidateLink(candidateId, actorUserId, nowUtc)` (now actually raises the existing but never-raised GuardianLinkRevokedEvent) and `UnlinkCandidate(candidateId, actorUserId, nowUtc)` (raises GuardianCandidateUnlinkedEvent). GuardianLink.Verify(nowUtc) and Revoke(nowUtc) replace the DateTime.UtcNow calls in Guardian.cs and GuardianLink.cs. GuardianCreatedEvent drops Email and FullName and carries CreatedByUserId (NFR-6). Setters become private and `public new Guid Id` is removed (Guardian.cs:10). The Link, Revoke and Unlink commands gain ActorUserId from the token. GuardianLinkVerifiedEvent is kept for PR3.
TESTS: GuardianTests.RevokeCandidateLink_RaisesGuardianLinkRevokedEventWithActor
  GuardianTests.LinkCandidate_RaisesGuardianLinkRequestedEvent
  GuardianTests.Create_StampsTimesFromSuppliedNow

## C24 refactor(sharedkernel): remove the DomainEvent base that stamped DateTime.UtcNow (D5)
FILES: apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Domain/DomainEvent.cs (delete)
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Domain/IDomainEvent.cs
DETAILS: Only M3 events used it (DomainEvent.cs:10 defaulted `OccurredAtUtc = DateTime.UtcNow`). All events now implement IDomainEvent with an explicit OccurredAtUtc passed from Clock. Update the IDomainEvent doc to say the aggregate supplies the time from Clock.
TESTS: Solution builds; git grep -n "DateTime.UtcNow" apps/api/src/Modules/{ExamAuthoring,Batch,Invite,Guardian} apps/api/src/SharedKernel/*Domain returns nothing

## C25 feat(sharedkernel): request-context port and assembly-scanned domain-event handler registration (FR-40)
FILES: apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/IRequestContext.cs (new)
  apps/api/src/Host/ExamPlatform.Api/HttpRequestContext.cs (new)
  apps/api/src/Host/ExamPlatform.Api/Program.cs
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Infrastructure/DomainEventHandlerRegistration.cs (new)
  apps/api/tests/SharedKernel/ExamPlatform.SharedKernel.UnitTests/DomainEventHandlerRegistrationTests.cs (new)
DETAILS: `IRequestContext { Guid? UserId; string? PrimaryRole; string? CorrelationId; }` is a port in SharedKernel.Application so any Application layer can attribute audit entries without seeing HttpContext. HttpRequestContext is the Host adapter, since the Host is the composition root and may reference SharedKernel.*. It uses IHttpContextAccessor and the shared claims helpers. CorrelationId is `Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier`, which fills the always-null CorrelationId gap for new audit entries. With no HttpContext (the --migrate-and-seed path) every value is null. Program.cs adds `builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<IRequestContext, HttpRequestContext>();`. `AddDomainEventHandlersFrom(this IServiceCollection, Assembly)` scans non-abstract classes implementing closed IDomainEventHandler<> and registers each interface as scoped, so a new handler needs no installer edit (OCP). InProcessDomainEventDispatcher already resolves GetServices(handlerType).
TESTS: DomainEventHandlerRegistrationTests.RegistersEveryClosedHandlerInterfaceOfAType
  DomainEventHandlerRegistrationTests.IgnoresAbstractAndOpenGenericTypes

## C26 feat(audit): record exam authoring changes in the audit trail (FR-40)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/ExamPlatform.Modules.ExamAuthoring.Application.csproj (+ Admin.Contracts ProjectReference)
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Audit/ExamAuthoringAuditTrail.cs (new)
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringModuleInstaller.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/ExamAuthoringAuditTrailTests.cs (new)
DETAILS: Handlers live in the module that owns the events, in its Application layer, and talk to Admin only through Admin.Contracts' IAuditLogger. The ADR allows Application to reference other modules' Contracts, and Admin.Application stays unaware of other modules, as its arch test requires. `sealed class ExamAuthoringAuditTrail(IAuditLogger auditLogger, IRequestContext requestContext) : IDomainEventHandler<ExamCreatedEvent>, IDomainEventHandler<ExamPublishedEvent>` records `new AuditEntry(evt.CreatedBy or evt.ActorUserId, requestContext.PrimaryRole, "ExamAuthoring.ExamCreated" or "ExamAuthoring.ExamPublished", "Exam", evt.ExamId.ToString(), metadata of ids and non-PII values only, requestContext.CorrelationId)`. The installer calls `services.AddDomainEventHandlersFrom(typeof(ExamAuthoringAuditTrail).Assembly)`. Why-comment: this runs after the module's commit via DomainEventsSaveChangesInterceptor, so an audit failure surfaces as an error after the change has persisted. That is the same accepted trade-off as ConsentService.cs:60-62, and an outbox is deferred per ADR 0001.
TESTS: ExamAuthoringAuditTrailTests.ExamCreated_RecordsEntryWithActorRoleAndCorrelation (NSubstitute IAuditLogger and IRequestContext)
  ExamAuthoringAuditTrailTests.ExamPublished_RecordsEntryWithActor

## C27 feat(audit): record batch changes in the audit trail (FR-40)
FILES: apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/ExamPlatform.Modules.Batch.Application.csproj
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Audit/BatchAuditTrail.cs (new)
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchModuleInstaller.cs
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/BatchAuditTrailTests.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/M3AuditTrailTests.cs (new)
  apps/api/tests/ExamPlatform.ArchitectureTests/ApplicationLayerTests.cs
DETAILS: Actions: Batch.Created, Batch.MemberAdded, Batch.Activated, Batch.Closed. EntityType is "Batch". Metadata carries examId and memberId, never email or phone (NFR-6, with a why-comment). Add an arch-test positive control showing the Batch.Application audit trail depends on Admin.Contracts.
TESTS: BatchAuditTrailTests.MemberAdded_MetadataHasNoEmailOrPhone
  BatchAuditTrailTests.Activated_UsesEventActorAndRequestRole
  M3AuditTrailTests.BatchLifecycle_WritesAuditEntriesAttributedToTokenSubject (an InstituteTeacher creates, adds a member and activates; a SuperAdmin calls GET /v1/admin/audit-logs?entityType=Batch; the three actions are present with ActorUserId = teacher and ActorRole = InstituteTeacher; AdminDbContext shows a non-null CorrelationId)
  ApplicationLayerTests.BatchApplication_DependsOnAdminContracts (positive control)

## C28 feat(audit): record invite changes in the audit trail without PII (FR-40, NFR-6)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/ExamPlatform.Modules.Invite.Application.csproj
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Audit/InviteAuditTrail.cs (new)
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteModuleInstaller.cs
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/InviteAuditTrailTests.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/M3AuditTrailTests.cs
DETAILS: Actions: Invite.Created, Invite.CodeIssued, Invite.Accepted, Invite.Declined, Invite.Revoked. EntityType is "Invite". Metadata holds examId, batchMemberId, codeId and expiresAt. It never holds Email, even though the events carry it, and never the code value.
TESTS: InviteAuditTrailTests.EveryHandledEvent_MetadataContainsNoEmailOrCode
  M3AuditTrailTests.InviteLifecycle_IsAudited_AndNoEntryContainsTheInviteEmail

## C29 feat(audit): record guardian changes in the audit trail without PII (FR-40, NFR-6)
FILES: apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/ExamPlatform.Modules.Guardian.Application.csproj
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Application/Audit/GuardianAuditTrail.cs (new)
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianModuleInstaller.cs
  apps/api/tests/Modules/Guardian/ExamPlatform.Modules.Guardian.UnitTests/GuardianAuditTrailTests.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/M3AuditTrailTests.cs
DETAILS: Actions: Guardian.Created, Guardian.LinkRequested, Guardian.LinkRevoked, Guardian.CandidateUnlinked. EntityType is "Guardian" or "GuardianLink". Metadata holds guardianId and candidateId. The consent and minor rules around guardians need explicit tests (section 18 compliance-sensitive).
TESTS: GuardianAuditTrailTests.LinkRevoked_RecordsCandidateAndGuardianIdsOnly
  M3AuditTrailTests.GuardianLinkRevoked_IsAudited_WithStaffActor

## C30 fix(admin): validate audit-log paging with a shared PageRequest (FR-40, NFR-5)
FILES: apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Paging/PageRequest.cs (new)
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Paging/InvalidPageRequestError.cs (new, 400 invalid_paging)
  apps/api/src/Modules/Admin/ExamPlatform.Modules.Admin.Application/Queries/SearchAuditLogsQuery.cs
  apps/api/src/Modules/Admin/ExamPlatform.Modules.Admin.Application/Queries/SearchAuditLogsHandler.cs
  apps/api/src/Modules/Admin/ExamPlatform.Modules.Admin.Application/Ports/IAuditLogRepository.cs
  apps/api/src/Modules/Admin/ExamPlatform.Modules.Admin.Infrastructure/AuditLogRepository.cs
  apps/api/src/Modules/Admin/ExamPlatform.Modules.Admin.Endpoints/AdminEndpoints.cs
  apps/api/tests/SharedKernel/ExamPlatform.SharedKernel.UnitTests/PageRequestTests.cs (new)
  apps/api/tests/ExamPlatform.IntegrationTests/AdminAuditPagingTests.cs (new)
DETAILS: `PageRequest.Create(int? page, int? pageSize, int defaultPageSize = 50, int maxPageSize = 200)` requires page >= 1 and 1 <= pageSize <= max, otherwise it throws InvalidPageRequestError. It exposes Page, PageSize and Skip. AdminEndpoints.cs:19 builds it from the query string. SearchAuditLogsQuery and the repository take a PageRequest instead of raw ints, so AuditLogRepository.cs:32 can never compute a negative OFFSET. Today page=0 produces a Postgres error and a 500, and pageSize has no cap. PR4 reuses PageRequest for its GET list endpoints.
TESTS: PageRequestTests.Create_Defaults_Page1Size50
  PageRequestTests.Create_PageZeroOrNegative_ThrowsInvalidPageRequestError
  PageRequestTests.Create_PageSizeZeroOrAboveMax_ThrowsInvalidPageRequestError
  PageRequestTests.Skip_IsPageMinusOneTimesSize
  AdminAuditPagingTests.AuditLogs_PageZero_Returns400InvalidPaging (was 500)
  AdminAuditPagingTests.AuditLogs_PageSizeAboveCap_Returns400

## C31 fix(host): return application/problem+json with traceId for domain errors (section 11, NFR-6)
FILES: apps/api/src/Host/ExamPlatform.Api/DomainExceptionHandler.cs
  apps/api/tests/ExamPlatform.IntegrationTests/ProblemDetailsTests.cs (new)
DETAILS: Inject IProblemDetailsService and write through `TryWriteAsync(new ProblemDetailsContext { HttpContext, ProblemDetails = { Status, Title = ErrorCode, Detail = Message, Instance = path } })`. Content-Type becomes application/problem+json, and the default writer adds the traceId extension. Title stays as ErrorCode, which the web's shared/problem-details.ts extractErrorMessage relies on. Log `{ErrorCode} ({StatusCode})` at Warning without passing the exception object: domain errors are expected, and exception messages can carry user input (NFR-6).
TESTS: ProblemDetailsTests.DomainError_ReturnsProblemJsonWithTitleStatusAndTraceId (POST /v1/batches/{random}/activate as ExamAdmin: 404, application/problem+json, title batch_not_found, traceId present)

## C32 docs: correct README and endpoint FR references for M3 (MediatR, RBAC matrix, seeding)
FILES: README.md
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchEndpoints.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteEndpoints.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Endpoints/GuardianEndpoints.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Batch.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs
  apps/api/src/Modules/Guardian/ExamPlatform.Modules.Guardian.Domain/Guardian.cs
DETAILS: Remove the 'CQRS via MediatR' claims (README around lines 75, 144, 374 and 427). Document the permission matrix and the `--migrate-and-seed` deployment step with a link to ADR 0002. Correct the FR IDs in doc headers: Batch is FR-50, not FR-17..19 or FR-21. Invite is FR-50a, not FR-20..21 or FR-23..25. Guardian is FR-45/FR-43, not FR-22..24 or FR-35/36.
TESTS: n/a (documentation)

## migrations
- Invite: DropInviteCreatedBy. Drops invite.Invites.CreatedBy (timestamptz junk column). Down re-adds it NOT NULL with default '-infinity'
- ExamAuthoring: MakeExamScheduleOptional. Makes examAuthoring.Exams.ScheduledStartTime and ScheduledEndTime nullable, then UPDATEs '-infinity' to NULL. Down fills NULL with '-infinity' and restores NOT NULL
- Invite: AddInviteRowVersion. Maps xmin as the concurrency token (shadow uint 'Version' with IsRowVersion). The expected SQL is a no-op; if EF scaffolds AddColumn xmin, remove that operation by hand and keep the snapshot
- Batch: UniqueActiveBatchMemberEmail. Pre-SQL lower(trim(Email)) plus soft-deleting active duplicates, keeping the earliest CreatedAt with Id as tie-break. Then a unique index on batch.BatchMembers(BatchId, Email) WHERE "IsDeleted" = false
- Guardian: UniqueActiveGuardianLink. Pre-SQL soft-deletes active duplicate links. Then a unique index on guardian.GuardianLinks(GuardianId, CandidateId) WHERE "IsDeleted" = false
- No Identity migration. New permissions and grants are reference data written by the idempotent IdentitySeeder at migrate-and-seed time, not by a schema migration
- Adding ApplyUtcDateTimeConversion/ApplyClientGeneratedGuidKeys and the explicit backing-field navigations should produce no schema diff. Verify with `dotnet ef migrations has-pending-model-changes` and do not commit empty migrations
- Generate each one with: dotnet ef migrations add <Name> --project src/Modules/<M>/ExamPlatform.Modules.<M>.Infrastructure --startup-project src/Host/ExamPlatform.Api --context <M>DbContext --output-dir Migrations (run from apps/api)

## reuse
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/Authorization/PermissionPolicyProvider.cs + PermissionAuthorizationHandler.cs: any 'permission:<code>' policy works by name; usage example at AdminEndpoints.cs:24
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/ClaimsPrincipalExtensions.cs and ConsentEndpoints.cs:40 CallerId: logic moved into SharedKernel.Application/Security
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/Repositories/UserRepository.cs:13-15 and RoleRepository.cs:10: the private Loaded() Include pattern
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/IdentityDbContext.cs:49,71,102-103: explicit HasField/UsePropertyAccessMode navigations plus the convention calls
- apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Infrastructure/UtcDateTimeConventions.cs and ClientGeneratedKeyConventions.cs
- apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Clock.cs; FakeClock pattern at apps/api/tests/Modules/Consent/ExamPlatform.Modules.Consent.UnitTests/FakeClock.cs
- Identity 'now' parameter pattern: Identity.Domain/OtpChallenge.cs:91 Verify(hash, nowUtc), User.Register(..., nowUtc), and UserRegisteredEvent(Guid, DateTime OccurredAtUtc) : IDomainEvent
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/Commands/AssignRoleHandler.cs: sealed handler with HandleAsync, `?? throw new XNotFoundError()`, and the AuditEntry shape
- apps/api/src/Modules/Admin/ExamPlatform.Modules.Admin.Contracts/IAuditLogger.cs + AuditEntry.cs
- apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/IDomainEventDispatcher.cs (IDomainEventHandler<T>) + SharedKernel.Infrastructure/InProcessDomainEventDispatcher.cs + DomainEventsSaveChangesInterceptor.cs, already attached to every M3 DbContext
- Existing typed errors: BatchNotFoundError, InvalidBatchConfigError, DuplicateMemberError, ExamNotFoundError, InvalidExamConfigError, DuplicateQuestionError, InviteNotFoundError, InvalidInviteCodeError, GuardianNotFoundError; mapped by apps/api/src/Host/ExamPlatform.Api/DomainExceptionHandler.cs
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/OtpCodeGenerator.cs: RandomNumberGenerator usage
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/LoginSessionIssuer.cs: used by the TestUsers integration helper to mint real tokens
- apps/api/tests/ExamPlatform.IntegrationTests/ApiFactory.cs and the AuthConsentAuditFlowTests.cs journey and audit-assertion pattern (lines 131-172)
- apps/api/tests/ExamPlatform.ArchitectureTests/*.cs MemberData rule shape
- docs/adr/template.md for ADR 0002

## covers
- IdentitySeeder.cs:19-22: returns early once any role exists, so later permissions never reach existing databases. Replaced by an idempotent upsert
- IdentitySeeder.cs:24-27: only 3 permissions seeded and only SuperAdmin has grants. Replaced by the full D3 catalog
- Program.cs:126: migrate and seed run only in Development, leaving no production path. Now --migrate-and-seed or Database:MigrateAndSeedOnStartup, documented in ADR 0002
- ExamAuthoringEndpoints.cs:15, BatchEndpoints.cs:15, InviteEndpoints.cs:15, GuardianEndpoints.cs:15: groups only RequireAuthorization() (OWASP A01). Now per-route permission policies
- ExamAuthoringEndpoints.cs:31,43; BatchEndpoints.cs:44,92; InviteEndpoints.cs:47,107: actor taken from the request body. Now JWT sub via the shared helper
- exam-api.service.ts:14, batch-api.service.ts:14, invite-api.service.ts:14: web sends createdBy/createdByUserId
- ConsentEndpoints.cs:40: CallerId uses `!`/Guid.Parse and can 500. Now the shared typed helper
- EFExamRepository.cs:15, EFBatchRepository.cs:21, InviteRepositoryAndUnitOfWork.cs:14, GuardianRepositoryAndUnitOfWork.cs:14: children never Included. Batch Activate always 400, Close unreachable, duplicate and capacity checks skipped; Invite Accept always 400, Revoke never revokes codes; Guardian duplicate links allowed and Revoke/Unlink silently 204
- GuardianHandlers.cs:72-77,86-88: silent no-op revoke/unlink returning 204 (section 1.2)
- EFBatchRepository.cs:28, InviteRepositoryAndUnitOfWork.cs:18, GuardianRepositoryAndUnitOfWork.cs:18, EFExamRepository.cs:20: not-found throws InvalidOperationException (500). Now the typed *NotFoundError (404)
- Invite.cs:68,85,97; Exam.cs:76; Guardian.cs:39; GuardianLink.cs:34,44: state guards throw InvalidOperationException (500). Now typed 409s
- CreateBatchHandler.cs:14, CreateExamHandler.cs:14, InviteHandlers.cs:16, GuardianHandlers.cs:16,19: ArgumentException (500). Now typed 400s in the domain
- GuardianHandlers.cs:62: VerifyGuardianLinkHandler throws NotImplementedException (500). Route and handler removed; PR3 re-introduces verification
- InviteHandlers.cs:52 / Invite.GenerateCode: ExpiryHours unvalidated, so huge values 500 and non-positive values create already-expired codes. Now bounded 1..720 with a typed 400
- Batch.cs:42 / BatchEndpoints.cs:50: case-sensitive duplicate check, no email or phone validation, Phone > 20 gives DbUpdateException 500
- DuplicateMemberError.cs:5: email embedded in an error message that is logged and returned to the client (NFR-6)
- DomainExceptionHandler.cs:25-40: logs the full exception message (PII) and writes application/json with no traceId
- ExamAuthoringDbContext.cs:12, BatchDbContext.cs:14, InviteDbContext.cs:12, GuardianDbContext.cs:12: ApplyUtcDateTimeConversion/ApplyClientGeneratedGuidKeys skipped
- CreateExamHandler.cs:20-21: DateTime.MinValue (Kind=Unspecified) placeholder schedule. Now a nullable schedule plus migration
- M3 domain uses DateTime.UtcNow (Batch.cs:34-97, BatchMember.cs:25-52, Invite.cs:36-109, InviteCode.cs:21-45, Guardian.cs, GuardianLink.cs, Exam.cs:41-82, ExamSection.cs:26, ExamQuestion.cs:19) and SharedKernel DomainEvent.cs:10 defaults to DateTime.UtcNow. Now Clock-driven
- Batch.cs:10, Invite.cs:10, Guardian.cs:10, Exam.cs:10: `public new Guid Id => base.Id` shadowing (critic's unverified EF-materialization risk). Removed
- Exam.cs:11-25, Batch.cs:11-19, Invite.cs:11-22, Guardian.cs:11-16: public setters let callers bypass invariants. Now private
- Invite.cs:18: junk `DateTime CreatedBy` column. Dropped via migration
- Invite.cs:115: System.Random invite codes. Now CSPRNG (unique index and lookup by code left to PR4)
- InviteDbContext.cs:16: no concurrency token, so a single-use accept can double-commit. xmin added
- BatchDbContext.cs:42, GuardianDbContext.cs:34: no DB uniqueness backing member and link invariants. Filtered unique indexes added
- Directory.Packages.props:40 and the 8 M3 csproj files: MediatR referenced although ADR 0001:64 rejected it. Removed
- M3 IUnitOfWork interfaces do not extend SharedKernel IUnitOfWork
- ContractsLayerTests.cs:26, EndpointsAndHostLayerTests.cs:29,45, DomainLayerTests.cs:39, InfrastructureLayerTests.cs:27, ApplicationLayerTests.cs: hard-coded module lists that exclude M3. Replaced by assembly discovery
- FR-40: zero IDomainEventHandler implementations, so M3 events reached nobody. Audit trails added for ExamAuthoring, Batch, Invite and Guardian with non-null CorrelationId and no PII
- Guardian.cs:41 / GuardianLinkRevokedEvent declared but never raised. Now raised
- AdminEndpoints.cs:19 / AuditLogRepository.cs:32: page and pageSize unvalidated, so page=0 gives a negative OFFSET 500 and pageSize is uncapped
- TestJwtTokenBuilder.cs:100 (audit cites :121): M3 tests used perm-less 'Admin' tokens that hid the access-control bug, with no 403 test. Now TestUsers with real grants plus 403 tests
- IModuleInstaller.cs:27-35 and Program.cs:121-123: docs claimed a production migration path that did not exist
- README/endpoint doc comments: mislabelled FR IDs (BatchEndpoints.cs:8 etc.) and false MediatR CQRS claims

## risks
- D2 placement: the claims helper and IRequestContext live in SharedKernel.Application, which already has a FrameworkReference to AspNetCore.App, so no new SharedKernel.Endpoints project is needed. The cost is that Application layers can technically call ClaimsPrincipal helpers too. Accepted; ADR 0002 could mention it
- D3 deviation, temporary: invite accept and decline are behind invite.manage instead of candidate self-service. Once the Include fix makes accept work, accept-by-GUID with no caller binding would be an IDOR. PR4 must replace them with an owner-bound POST /v1/invites/{code}/accept. Likewise, every guardian route is staff-only (guardian.link.manage) until PR3
- Permissions are baked into the JWT at login, so existing sessions keep old permission sets until users log in again after the seeder runs
- The seeder is additive only and never revokes or changes RequiresTwoFactor. Taking a grant away needs a deliberate follow-up
- With Database:MigrateAndSeedOnStartup=true on several replicas at once, the seeders can race on unique Role.Name and Permission.Code. The documented path is one --migrate-and-seed job before rollout
- Audit writes are not atomic: handlers run after the module's commit, in a separate AdminDbContext transaction. An audit failure returns 500 for a change that did persist. This is the same trade-off ConsentService and AssignRoleHandler already make; an outbox is deferred per ADR 0001
- Merge conflicts with PR1: IdentityEndpoints.cs, Program.cs, IdentitySeeder.cs, TestJwtTokenBuilder.cs, the ClaimsPrincipalExtensions deletion and LoginSessionIssuer (which TestUsers uses). Rebase onto PR1 first, and adapt TestUsers if PR1 changed Issue()'s signature or added a sid check
- Some migrations alter data on existing dev databases: '-infinity' schedules become NULL, emails are lower-cased, and duplicate members or links are soft-deleted. Down migrations are not lossless for the dedupe
- xmin mapping: Npgsql may scaffold AddColumn 'xmin'. Review the migration and strip it if so
- Removing `new Guid Id`: EF then maps Entity.Id (protected init) the way Identity entities do. Verify with has-pending-model-changes (expect none) and the integration suite
- Exam schedule becomes nullable, which changes the ExamDto contract. The web model is updated in the same commit, and PR4's scheduling code must treat null as 'unscheduled'
- Architecture test discovery relies on module DLLs reaching the test output through the Host ProjectReference. A module not listed in the Host would not be discovered; the known-modules sanity test guards against a vacuous pass
- Integration test cost: each new IClassFixture<ApiFactory> starts its own Postgres container. Consider an xUnit collection fixture if CI time becomes a problem
- Commit trailers: requirements section 1.1 and D10 forbid Co-authored-by and AI trailers, which conflicts with the harness attribution reminder. Follow the user's project rule and confirm with the user if in doubt
- InvalidLinkTokenError stays unused in PR2, kept for PR3. It is dead code until then

## outOfScope
- M2 Question Bank and M4-M7 modules (only the seams remain: exam.publish and batch.read are seeded, PageRequest is reusable, and events carry actor ids)
- PR3: consent IDOR and consent.manage enforcement, guardian accounts (Guardian.UserId), OTP link verification replacing the removed verify route, guardian consent into the ledger, the under-18 gate, Identity.Contracts and Guardian.Contracts, Consent.Granted auditing, and CorrelationId/ActorRole for ConsentService audit entries
- PR4: GET list/detail endpoints the web calls, FR-12 config and FR-13 schedule endpoints with invariants, publish endpoint, exam-to-batch assignment (FR-14), accept by code string bound to the caller, Enrollment on InviteAccepted, InviteExpiredError/Expired status, unique index and hashing for InviteCodes.Code, CSV roster import (FR-50/50a) including RosterValidator's bare catch, nullable SeriesId, web enum/DTO alignment, role-aware web routing
- PR1: Identity items (OTP attempts/replay, admin 2FA bypass, sid validation, Enum.Parse 500s, DOB validation, auth rate limits, LoggingOtpSender PII), plus CorrelationId in AssignRoleHandler's audit entry
- Cross-module existence checks (exam exists before a batch or invite is created, batch member exists before an invite) that need ExamAuthoring.Contracts or Batch.Contracts
- HasDefaultSchema / per-module __EFMigrationsHistory (only documented in ADR 0002)
- A transactional outbox for audit atomicity
- Security headers, HSTS, forwarded headers, and OpenAPI/Scalar only in Development (NFR-5 items not assigned to any PR; suggest a separate SP/chore)
- A banned-API analyzer (Microsoft.CodeAnalysis.BannedApiAnalyzers) to forbid DateTime.UtcNow repo-wide
- Unique Guardians.Email (PR3 redesigns the guardian identity)
- Audit search filters (action/date/entityId) and an admin audit-log UI

## verification
- cd apps/api && dotnet restore ExamPlatform.slnx && dotnet build ExamPlatform.slnx -c Release (Nullable warnings are errors and GenerateDocumentationFile is on)
- cd apps/api && dotnet test ExamPlatform.slnx -c Release (unit, architecture and Testcontainers integration tests; Docker must be running)
- cd apps/api && dotnet test tests/ExamPlatform.ArchitectureTests -c Release, then confirm test cases appear for ExamAuthoring, Batch, Invite and Guardian
- cd apps/api && dotnet test tests/ExamPlatform.IntegrationTests -c Release --filter "FullyQualifiedName~M3AuthorizationTests|FullyQualifiedName~BatchFlowTests|FullyQualifiedName~InviteFlowTests|FullyQualifiedName~GuardianFlowTests|FullyQualifiedName~M3AuditTrailTests|FullyQualifiedName~RbacSeedingTests"
- For each of ExamAuthoring, Batch, Invite, Guardian and Identity: dotnet ef migrations has-pending-model-changes --project src/Modules/<M>/ExamPlatform.Modules.<M>.Infrastructure --startup-project src/Host/ExamPlatform.Api --context <M>DbContext (expect: no changes)
- dotnet ef migrations script <PreviousMigration> <NewMigration> --idempotent --context InviteDbContext ... to review the SQL: the xmin migration is a no-op and the dedupe SQL precedes CREATE UNIQUE INDEX
- Non-dev seeding smoke test: ASPNETCORE_ENVIRONMENT=Production ConnectionStrings__Postgres=<local> Jwt__SigningKey=<32+ bytes> dotnet run --project src/Host/ExamPlatform.Api -- --migrate-and-seed should exit 0 and log each module; then `select count(*) from identity."Permissions"` returns 9 and a second run changes nothing
- git grep -n MediatR -- apps/api returns nothing
- git grep -nE "DateTime\.UtcNow|InvalidOperationException|ArgumentException|NotImplementedException" -- apps/api/src/Modules/ExamAuthoring apps/api/src/Modules/Batch apps/api/src/Modules/Invite apps/api/src/Modules/Guardian returns nothing (excluding Migrations)
- git grep -nE "createdBy|createdByUserId" -- apps/web/src/app/*/*-api.service.ts returns nothing
- cd apps/web && npm ci && npm run lint && npm run build && npm test -- --watch=false
- Manual check with a Candidate token from the real OTP login: POST /v1/batches returns 403; with an InstituteTeacher token, POST /v1/batches returns 201 and GET /v1/admin/audit-logs?entityType=Batch as SuperAdmin shows Batch.Created attributed to the teacher
- git log --format=%B <PR1-tip>..HEAD | grep -i "co-authored" returns nothing

