# PR4 SP/feature/m3-api-completion
PR4: M3 API completion. Adds the exam, batch and invite endpoints the web UI already calls, FR-12 config, FR-13 scheduling, FR-14 exam-to-batch assignment and invite-only enrollment, FR-50 CSV roster import, FR-50a invite accept by code, and the web contract fixes plus role-aware routing (FR-2 UI). Stacked on PR3.

Summary of design decisions (to be recorded in a new docs/adr/0002-m3-enrollment-scheduling-ownership.md):
(1) ExamAuthoring owns the ExamBatchAssignment many-to-many. Batch.ExamId is dropped.
(2) The Invite module owns Enrollment(UserId, ExamId, SourceInviteId) and IEnrollmentPolicy (InviteOnlyEnrollmentPolicy). M4 reads enrollments through Invite.Contracts IEnrollmentQuery.
(3) New leaf Contracts projects: ExamAuthoring.Contracts (IExamCatalog), Batch.Contracts (IBatchDirectory) and Invite.Contracts (IEnrollmentQuery plus integration events). Identity.Contracts, which PR3 creates, gains IUserContactLookup.
(4) Time zones go through a SharedKernel ITimeZoneConverter backed by NodaTime's embedded TZDB. The reason: Directory.Build.props:10 sets InvariantGlobalization=true, which disables ICU, so TimeZoneInfo cannot resolve IANA ids such as "Asia/Kolkata" on Windows.
(5) Schedules are sent as local wall-clock strings plus an IANA zone. The server converts them to UTC with strict DST handling and stores UTC.
(6) Invite codes are 10 characters from a 32-character alphabet, generated with a CSPRNG and stored only as a SHA-256 hash with a unique index. The plaintext is returned once. The Invite row has an xmin concurrency token.
(7) Accepting an invite writes the invite state change and the Enrollment in one transaction. The cross-module side effects (BatchMember registered or withdrawn, audit) run through IDomainEventHandler on Contracts integration events.
(8) Institute/Teacher scoping: callers see and mutate only batches they created, unless they hold the new batch.read.all permission.

Baseline assumed from PR2/PR3:
- MediatR is gone. Handlers are sealed classes with HandleAsync(command, ct).
- Repositories Include child collections and throw the typed *NotFoundError.
- The M3 DbContexts apply the UTC and ClientGeneratedGuidKeys conventions.
- Clock is injected, and domain methods take a `now` parameter.
- The PR2 permission codes are seeded by an idempotent-upsert IdentitySeeder.
- A shared sub-claim helper exists (D2).
- TestJwtTokenBuilder has a perm-claims variant.
- An audit-handler pattern over IAuditLogger exists.
- PR3 has created Identity.Contracts.

Where a step duplicates something PR2 already did, the engineer verifies it and skips that step. Commits carry FR IDs and no Co-authored-by or AI trailer (requirements section 1.1 overrides the harness reminder).
REQ: FR-2, FR-11, FR-12, FR-13, FR-14, FR-16, FR-40, FR-43, FR-50, FR-50a, NFR-5, NFR-11, SEC10-Enrollment, SEC11-EnrollmentPolicy, SEC11-Clock, SEC11-TypedErrors-InviteExpiredError, SEC13-POST-invites-code-accept, SEC13-GET-exams, SEC13-Admin-exams-batches-invites

## C1 feat(shared-kernel): add PageRequest/PagedResult paging primitives with typed validation (NFR-11, section 11 typed errors)
FILES: apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Paging/PageRequest.cs
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Paging/PagedResult.cs
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Paging/InvalidPagingError.cs
  apps/api/tests/SharedKernel/ExamPlatform.SharedKernel.UnitTests/ExamPlatform.SharedKernel.UnitTests.csproj
  apps/api/tests/SharedKernel/ExamPlatform.SharedKernel.UnitTests/PageRequestTests.cs
  apps/api/ExamPlatform.slnx
DETAILS: - PageRequest.Create(int? page, int? pageSize) defaults to page 1, pageSize 25. It throws InvalidPagingError (a DomainException, 400, 'invalid_paging') when page < 1 or pageSize is outside 1..100.
- PagedResult<T>(Items, Page, PageSize, TotalCount) is serialized camelCase.
- These live in SharedKernel.Application, not Contracts, because only module-internal queries and endpoints page.
- Creates the new SharedKernel unit test project (xunit, same props as the other module test projects) and adds it to the slnx.
- If PR2 already added a paging helper for AdminEndpoints.cs:19, reuse it and drop this commit.
TESTS: PageRequestTests.Create_NullArguments_UsesDefaults
  PageRequestTests.Create_PageBelowOne_ThrowsInvalidPagingError
  PageRequestTests.Create_PageSizeAboveMax_ThrowsInvalidPagingError
  PageRequestTests.Skip_ComputesOffsetFromPage

## C2 feat(shared-kernel): IANA time-zone conversion via NodaTime behind ITimeZoneConverter (FR-13, section 17 default Asia/Kolkata, UTC storage)
FILES: apps/api/Directory.Packages.props
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Time/ITimeZoneConverter.cs
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Time/UnknownTimeZoneError.cs
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Time/InvalidLocalDateTimeError.cs
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Time/PlatformTimeZones.cs
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Infrastructure/ExamPlatform.SharedKernel.Infrastructure.csproj
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Infrastructure/NodaTimeZoneConverter.cs
  apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Infrastructure/SharedKernelServiceCollectionExtensions.cs
  apps/api/tests/SharedKernel/ExamPlatform.SharedKernel.UnitTests/NodaTimeZoneConverterTests.cs
  .github/workflows/ci.yml
DETAILS: - Adds PackageVersion NodaTime 3.2.x (latest 3.x at implementation time) and references it from SharedKernel.Infrastructure only.
- The ITimeZoneConverter port exposes:
  - bool IsKnownZone(string ianaId)
  - DateTime ToUtc(string localIsoDateTime, string ianaId): parses 'yyyy-MM-ddTHH:mm[:ss]' with LocalDateTimePattern and uses InZoneStrictly.
  - string ToLocalIso(DateTime utc, string ianaId): used to echo values back.
- UnknownTimeZoneError: 400, 'unknown_time_zone'.
- InvalidLocalDateTimeError: 400, 'invalid_local_datetime'. Thrown for unparsable strings, DST-skipped times and DST-ambiguous times.
- PlatformTimeZones.Default = "Asia/Kolkata".
- Why-comment (required): TimeZoneInfo.FindSystemTimeZoneById cannot resolve IANA ids on Windows when InvariantGlobalization=true (Directory.Build.props:10), because IANA-to-Windows mapping needs ICU. On Linux it depends on the image shipping tzdata. NodaTime's embedded TZDB is OS-independent and deterministic. Strict mapping rejects ambiguous and skipped instants instead of silently shifting an exam by an hour.
- AddSharedKernel registers the converter as a singleton.
- CI: adds a small 'unit-tests-windows' job on windows-latest that runs only tests/SharedKernel/ExamPlatform.SharedKernel.UnitTests. This proves IANA resolution on Windows. The Testcontainers suite stays Linux-only.
TESTS: NodaTimeZoneConverterTests.ToUtc_AsiaKolkata_SubtractsFiveHoursThirty (2026-10-05T10:00 maps to 04:30Z)
  NodaTimeZoneConverterTests.ToUtc_ResultKindIsUtc
  NodaTimeZoneConverterTests.ToUtc_UnknownZone_ThrowsUnknownTimeZoneError
  NodaTimeZoneConverterTests.ToUtc_AmericaNewYorkSpringForwardGap_ThrowsInvalidLocalDateTimeError
  NodaTimeZoneConverterTests.ToUtc_AmericaNewYorkFallBackOverlap_ThrowsInvalidLocalDateTimeError
  NodaTimeZoneConverterTests.ToUtc_MalformedString_ThrowsInvalidLocalDateTimeError
  NodaTimeZoneConverterTests.IsKnownZone_AsiaKolkata_TrueOnEveryOs (executed on ubuntu and on windows-latest in CI)
  NodaTimeZoneConverterTests.ToLocalIso_RoundTripsToUtc

## C3 feat(web): decode perm/role claims and add permissionGuard, permission-aware nav and role landing (FR-2)
FILES: apps/web/src/app/auth/jwt.ts
  apps/web/src/app/auth/jwt.spec.ts
  apps/web/src/app/auth/auth-session.service.ts
  apps/web/src/app/auth/auth-session.service.spec.ts
  apps/web/src/app/auth/permission.guard.ts
  apps/web/src/app/auth/permission.guard.spec.ts
  apps/web/src/app/auth/landing.ts
  apps/web/src/app/auth/landing.spec.ts
  apps/web/src/app/auth/login/login.ts
  apps/web/src/app/auth/verify-otp/verify-otp.ts
  apps/web/src/app/app.routes.ts
  apps/web/src/app/app.html
  apps/web/src/app/app.ts
  apps/web/src/app/app.spec.ts
DETAILS: - jwt.ts (fixes jwt.ts:2-6):
  - DecodedSession gains permissions: string[], read from 'perm', which may be a string or an array.
  - It also gains roles: string[], read from 'role' or the long http://schemas.microsoft.com/ws/2008/06/identity/claims/role key.
  - The comment keeps the point that this is UI-only. The server policies stay authoritative.
- AuthSessionService.hasPermission(code) is a computed helper.
- permission.guard.ts exports a requirePermission(...codes) CanActivateFn:
  - Unauthenticated callers go to /login?returnUrl=...
  - Callers without the permission get a UrlTree to landingPath(session).
- landing.ts landingPath(session): /exams if exam.manage, else /batches if batch.read, else /invites if invite.manage, else /home.
- login.ts:89 and verify-otp.ts:51 default to landingPath() instead of '/profile'.
- app.routes.ts (fixes app.routes.ts:30-89, which only used authGuard):
  - '' redirects authenticated users to their landing page via a redirect guard, otherwise to /login.
  - exams/** requires exam.manage.
  - batches/** requires batch.read (create and roster require batch.manage).
  - invites (list and create) require invite.manage.
  - The /home, /invites/accept and exams/:id routes are added in later commits.
- app.html (fixes app.html:3-12): nav shows Exams, Batches and Invites only when the permission is present, and 'My exams' for everyone signed in.
TESTS: jwt.spec: decodes single perm string and perm array; missing perm -> []; reads short and long role keys
  auth-session.service.spec: hasPermission true/false
  permission.guard.spec: unauthenticated -> /login with returnUrl; authenticated without perm -> landing UrlTree; with perm -> true
  landing.spec: picks /exams, /batches, /invites, /home by permission precedence
  app.spec: nav hides staff links for a candidate token (buildFakeJwt from auth/testing/fake-jwt.ts)

## C4 fix(exam-authoring): make SeriesId optional end-to-end so blank series no longer 400s (FR-11)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringEndpoints.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/CreateExamCommand.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/CreateExamHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Dtos/CreateExamRequest.cs
  apps/web/src/app/exam-authoring/exam-builder/exam-builder.ts
  apps/web/src/app/exam-authoring/exam.models.ts
  apps/api/tests/ExamPlatform.IntegrationTests/ExamAuthoringFlowTests.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/CreateExamHandlerTests.cs
DETAILS: - CreateExamRequest.SeriesId (ExamAuthoringEndpoints.cs:40) and CreateExamCommand.SeriesId (CreateExamCommand.cs:8) become Guid?.
- Deletes the dead duplicate Application/Dtos/CreateExamRequest.cs.
- The handler rejects Guid.Empty with InvalidExamConfigError, so only null means 'no series'.
- Web (fixes exam-builder.ts:135/160):
  - seriesId is sent only when non-blank, otherwise null.
  - A GUID pattern validator is added with an inline error.
  - The error text now uses extractErrorMessage (shared/problem-details.ts).
TESTS: ExamAuthoringFlowTests.CreateExam_WithoutSeriesId_Returns201WithNullSeries
  ExamAuthoringFlowTests.CreateExam_WithEmptyStringSeries_IsNotSentByWeb (covered by web spec below)
  CreateExamHandlerTests.Handle_GuidEmptySeries_ThrowsInvalidExamConfigError
  web: exam-builder.spec submits seriesId null when blank and blocks non-GUID input

## C5 feat(exam-authoring): Draft-only edit guards, validated ExamConfig/MarkingScheme, section timer invariant, per-question marks (FR-12, section 4)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exam.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/ExamConfig.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/MarkingScheme.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/ExamSection.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/ExamQuestion.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Events/ExamConfigUpdatedEvent.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exceptions/ExamNotEditableError.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exceptions/SectionNotFoundError.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exceptions/ExamQuestionNotFoundError.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/ExamConfigTests.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/ExamEditingTests.cs
DETAILS: ExamConfig and MarkingScheme validation:
- ExamConfig gets a static Create(...) that validates and throws InvalidExamConfigError (400):
  - TotalTimeSeconds is null or 60..86400.
  - MaxAttempts >= 1 and MaxRetakes >= 0.
  - Mode=Scheduled requires ResultReleaseTime (UTC Kind). Other modes require it to be null.
- MarkingScheme.Create validates:
  - CorrectMarks > 0.
  - IncorrectMarks between -CorrectMarks and 0. Why-comment: negative marking is stored signed so graders add it directly.
  - UnattemptedMarks <= 0.
- The parameterless constructors stay as EF/default paths.

Exam aggregate:
- Setters become private (fixes Exam.cs:11-25) unless PR2 already did this.
- UpdateConfig(config, actorUserId, now), AddSection(name, timeSeconds, now), UpdateSection, RemoveSection(sectionId), AddQuestion(sectionId, questionVersionId, marks?, now) and RemoveQuestion(sectionId, examQuestionId) all:
  - throw ExamNotEditableError (409, 'exam_not_editable') when Status != Draft (fixes Exam.cs:47-69 and ExamSection.cs:29-43);
  - throw SectionNotFoundError / ExamQuestionNotFoundError (404) instead of silent no-ops (fixes RemoveSection and RemoveQuestion swallowing missing ids).
- Invariant: if any section has TimeSeconds, TotalTimeSeconds must be set and the section total must be <= TotalTimeSeconds. It is checked in both UpdateConfig and section changes.
- Order becomes max(Order)+1 rather than Count+1, so a removal does not produce duplicate orders.
- ExamQuestion gets decimal? Marks (the section 10 data model). Null means 'use MarkingScheme.CorrectMarks'.
- Raises ExamConfigUpdatedEvent(ExamId, ActorUserId, now), which the audit commit later consumes.
TESTS: ExamConfigTests.Create_MaxAttemptsZero_Throws
  ExamConfigTests.Create_NegativeRetakes_Throws
  ExamConfigTests.Create_ScheduledReleaseWithoutTime_Throws
  ExamConfigTests.Create_InstantReleaseWithTime_Throws
  ExamConfigTests.MarkingScheme_PositiveIncorrectMarks_Throws
  ExamConfigTests.MarkingScheme_PenaltyLargerThanReward_Throws
  ExamEditingTests.UpdateConfig_WhenPublished_ThrowsExamNotEditable
  ExamEditingTests.AddSection_WhenPublished_ThrowsExamNotEditable
  ExamEditingTests.SectionTimersExceedTotal_ThrowsInvalidExamConfig
  ExamEditingTests.RemoveSection_Unknown_ThrowsSectionNotFound
  ExamEditingTests.AddSection_AfterRemoval_UsesMaxOrderPlusOne
  ExamEditingTests.AddQuestion_Duplicate_ThrowsDuplicateQuestionError

## C6 feat(exam-authoring): ExamSchedule value object, publish readiness checks and Archive lifecycle (FR-13, exam status lifecycle)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/ExamSchedule.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exam.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Events/ExamScheduledEvent.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Events/ExamPublishedEvent.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Events/ExamArchivedEvent.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exceptions/InvalidExamScheduleError.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exceptions/ExamNotPublishableError.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/ExamScheduleTests.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/ExamLifecycleTests.cs
DETAILS: ExamSchedule:
- ExamSchedule.Create(startUtc, endUtc, lateEntryDeadlineUtc?, timeZoneId, nowUtc) requires Kind=Utc and enforces:
  - start > now;
  - end > start;
  - start < lateEntry <= end.
- Violations throw InvalidExamScheduleError (400, 'invalid_exam_schedule').
- Why-comment: all instants are UTC. TimeZoneId is kept only to render local times and to interpret future local inputs (section 17).
- The zone id is validated earlier, by ITimeZoneConverter, in the handler.

Exam.Schedule(schedule, actorUserId, now):
- Draft-only.
- Maps onto the existing ScheduledStartTime/ScheduledEndTime/LateEntryDeadline/TimeZone columns.
- Raises ExamScheduledEvent.

Exam.Publish(actorUserId, now) collects every failure reason and throws ExamNotPublishableError (422, 'exam_not_publishable', with a Reasons list) (fixes Exam.cs:73-84):
- not Draft;
- no sections;
- any section without questions;
- no schedule;
- schedule start <= now;
- Scheduled result release time earlier than schedule end.
- It also replaces the InvalidOperationException at Exam.cs:76.
- ExamPublishedEvent gains ActorUserId and StartUtc.

Exam.Archive(actorUserId, now):
- Allowed from Draft or Published.
- Archived to Archived throws ExamNotEditableError.
- Raises ExamArchivedEvent.
TESTS: ExamScheduleTests.Create_EndBeforeStart_Throws
  ExamScheduleTests.Create_StartInPast_Throws (FakeClock)
  ExamScheduleTests.Create_LateEntryAfterEnd_Throws
  ExamScheduleTests.Create_LateEntryBeforeStart_Throws
  ExamScheduleTests.Create_NonUtcKind_Throws
  ExamLifecycleTests.Schedule_WhenPublished_ThrowsExamNotEditable
  ExamLifecycleTests.Publish_WithoutSchedule_ListsMissingScheduleReason
  ExamLifecycleTests.Publish_SectionWithoutQuestions_ListsReason
  ExamLifecycleTests.Publish_ScheduledReleaseBeforeEnd_ListsReason
  ExamLifecycleTests.Publish_Valid_SetsPublishedAndRaisesEvent
  ExamLifecycleTests.Archive_FromPublished_Succeeds
  ExamLifecycleTests.Archive_Twice_Throws

## C7 feat(exam-authoring): migration for question marks, question uniqueness and nullable schedule (FR-12, FR-13)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/ExamAuthoringDbContext.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/Migrations/<timestamp>_M3ExamAuthoringCompletion.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/Migrations/<timestamp>_M3ExamAuthoringCompletion.Designer.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/Migrations/ExamAuthoringDbContextModelSnapshot.cs
DETAILS: - Maps ExamQuestion.Marks as numeric(6,2), nullable.
- Adds a unique index on ExamQuestions(SectionId, QuestionVersionId), backing the in-memory duplicate guard (audit DATA-MODEL-EXAM gap).
- Maps MarkingScheme precision numeric(6,2).
- If PR2 did not already make ScheduledStartTime/ScheduledEndTime nullable, do it here (CreateExamHandler.cs:20-21 wrote DateTime.MinValue). The migration's Up also runs UPDATE ... SET NULL where the value is '-infinity' or '0001-01-01'.
- Generated with dotnet ef migrations add M3ExamAuthoringCompletion (ExamAuthoringDbContext).
TESTS: Covered by integration tests in the following commits (migrations run in ApiFactory)
  Verification: dotnet ef migrations has-pending-model-changes --context ExamAuthoringDbContext reports none

## C8 feat(exam-authoring): paginated GET /v1/exams and GET /v1/exams/{id} with exam.manage policy; align web exam models (FR-11, FR-12, FR-13, FR-2)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Ports/IExamReadRepository.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Queries/ListExamsQuery.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Queries/ListExamsHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Queries/GetExamDetailQuery.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Queries/GetExamDetailHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Dtos/ExamDto.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Dtos/SectionDto.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Dtos/ExamMappings.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/Repositories/ExamReadRepository.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringEndpoints.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringModuleInstaller.cs
  apps/web/src/app/shared/paged-result.ts
  apps/web/src/app/exam-authoring/exam.models.ts
  apps/web/src/app/exam-authoring/exam-api.service.ts
  apps/web/src/app/exam-authoring/exam-api.service.spec.ts
  apps/web/src/app/exam-authoring/exam-list/exam-list.ts
  apps/api/tests/ExamPlatform.IntegrationTests/ExamAuthoringFlowTests.cs
DETAILS: Backend:
- IExamReadRepository runs AsNoTracking projections: page and count with an optional ExamStatus filter ordered by UpdatedAt desc, and a detail read with sections, questions and assignments.
- ListExamsHandler returns PagedResult<ExamSummaryDto>: Id, Name, Status, SeriesId, StartUtc, EndUtc, TimeZone, SectionCount, QuestionCount, UpdatedAt.
- GetExamDetailHandler returns ExamDetailDto:
  - the ExamSummary fields;
  - Config, with MarkingSchemeDto as an object;
  - ScheduleDto with UTC values plus local echoes via ITimeZoneConverter.ToLocalIso;
  - Sections[] with questions {id, questionVersionId, order, marks};
  - AssignedBatchIds[] (empty until the assignment commit).
- It throws ExamNotFoundError (404) when the exam is missing.
- Routes: GET /v1/exams?status=&page=&pageSize= and GET /v1/exams/{examId:guid}, both RequireAuthorization("permission:exam.manage") (D3).
- The existing Results.Created Location (ExamAuthoringEndpoints.cs:34) now resolves to a real route.

Web (fixes exam.models.ts:1-3,41-59):
- ExamStatus is 'Draft'|'Published'|'Archived'.
- ResultReleaseMode is 'Instant'|'Scheduled'|'Manual'.
- MarkingScheme is {correctMarks, incorrectMarks, unattemptedMarks}.
- SectionDto and ExamQuestionDto match the backend. Date fields are typed string (ISO).
- getExams(page, status) returns PagedResult<ExamSummaryDto>.
- exam-list: pagination controls; status badge classes for the real enum; the View button becomes routerLink ['/exams', id] (fixes exam-list.ts:100); errors via extractErrorMessage.
- The spec is switched to provideHttpClientTesting.
TESTS: ExamAuthoringFlowTests.ListExams_AsExamAdmin_ReturnsPagedResultContainingCreatedExam
  ExamAuthoringFlowTests.ListExams_FilterByStatus_ReturnsOnlyMatching
  ExamAuthoringFlowTests.ListExams_AsCandidate_Returns403
  ExamAuthoringFlowTests.GetExam_Unknown_Returns404ProblemDetailsExamNotFound
  ExamAuthoringFlowTests.ListExams_PageSizeTooLarge_Returns400InvalidPaging
  ListExamsHandlerTests.Handle_MapsMarkingSchemeAsObject
  web: exam-api.service.spec asserts GET /v1/exams?page=1&pageSize=25 and PagedResult typing

## C9 feat(exam-authoring): PUT /v1/exams/{id}/config for all FR-12 settings (FR-12)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/UpdateExamConfigCommand.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/UpdateExamConfigHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringEndpoints.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamRequests.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringModuleInstaller.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/UpdateExamConfigHandlerTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/ExamAuthoringFlowTests.cs
DETAILS: - UpdateExamConfigRequest fields: totalTimeSeconds?, shuffleQuestions, shuffleOptions, sectionLockEnabled, calculatorAllowed, scratchpadAllowed, maxAttempts, maxRetakes, resultReleaseMode, resultReleaseAtLocal? and markingScheme {correctMarks, incorrectMarks, unattemptedMarks}.
- resultReleaseAtLocal is interpreted in the exam's TimeZone via ITimeZoneConverter.
- The handler loads the tracked aggregate, builds the config with ExamConfig.Create and MarkingScheme.Create, calls exam.UpdateConfig(config, actor from 'sub', clock.UtcNow), saves, and returns ExamDetailDto.
- Route: PUT /v1/exams/{examId:guid}/config, permission exam.manage.
- Proctoring profile (FR-12 last item) is deliberately excluded; the section 8 / M6 seam is noted in ADR 0002.
- All request records move into ExamRequests.cs.
TESTS: UpdateExamConfigHandlerTests.Handle_ScheduledRelease_ConvertsLocalReleaseTimeInExamZone
  UpdateExamConfigHandlerTests.Handle_UsesClockAndCallerAsActor
  ExamAuthoringFlowTests.UpdateConfig_Valid_PersistsAllFr12FieldsAndReturnsThemFromGet
  ExamAuthoringFlowTests.UpdateConfig_ScheduledReleaseWithoutTime_Returns400InvalidExamConfig
  ExamAuthoringFlowTests.UpdateConfig_AsCandidate_Returns403

## C10 feat(exam-authoring): section and manual question-reference endpoints behind an M2 question-version seam (FR-11 manual, FR-12 per-section timers, section 4)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Ports/IQuestionVersionCatalog.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/QuestionBank/OpaqueQuestionVersionCatalog.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/AddSectionCommand.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/AddSectionHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/UpdateSectionCommand.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/UpdateSectionHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/RemoveSectionCommand.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/RemoveSectionHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/AddExamQuestionCommand.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/AddExamQuestionHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/RemoveExamQuestionCommand.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/RemoveExamQuestionHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Dtos/SectionDto.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringEndpoints.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamRequests.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringModuleInstaller.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/AddExamQuestionHandlerTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/ExamAuthoringFlowTests.cs
DETAILS: Question-version seam:
- IQuestionVersionCatalog.EnsureReferencableAsync(IReadOnlyCollection<Guid> ids, ct) is the seam M2 (Question Bank) will implement: the version exists, is approved and is not retired.
- The M3 adapter OpaqueQuestionVersionCatalog rejects only Guid.Empty, with InvalidExamConfigError.
- It has a why-comment that M3 treats QuestionVersionId as an opaque reference until QuestionBank.Contracts exists, and that it must be swapped in the installer, not bypassed.
- FR-11 rule-based generation will be a future GenerateSectionFromRulesCommand over the same port. No speculative interface is added now (documented in ADR 0002).

Endpoints (all exam.manage):
- POST /v1/exams/{id}/sections {name, timeSeconds?} returns 201 SectionDto. The existing unused AddSectionRequest is reused.
- PUT /v1/exams/{id}/sections/{sectionId:guid} {name, timeSeconds?}
- DELETE /v1/exams/{id}/sections/{sectionId:guid} returns 204.
- POST /v1/exams/{id}/sections/{sectionId:guid}/questions {questionVersionId, marks?} returns 201 ExamQuestionDto. AddQuestionRequest's Order is removed, because order is appended by the domain.
- DELETE /v1/exams/{id}/sections/{sectionId:guid}/questions/{examQuestionId:guid}

Typed responses: 409 from ExamNotEditableError and DuplicateQuestionError; 404 from SectionNotFoundError.
TESTS: AddExamQuestionHandlerTests.Handle_CallsQuestionVersionCatalogBeforeMutating
  AddExamQuestionHandlerTests.Handle_EmptyVersionId_Throws
  ExamAuthoringFlowTests.AddSectionsAndQuestions_ThenGetDetail_ReturnsOrderedSectionsWithQuestions
  ExamAuthoringFlowTests.AddQuestion_DuplicateVersionInSection_Returns409
  ExamAuthoringFlowTests.RemoveSection_Unknown_Returns404
  ExamAuthoringFlowTests.SectionTimersExceedTotal_Returns400

## C11 feat(exam-authoring): PUT /v1/exams/{id}/schedule with IANA zone and late-entry cutoff; fix web scheduler time-zone handling (FR-13)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/ScheduleExamCommand.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/ScheduleExamHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringEndpoints.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamRequests.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringModuleInstaller.cs
  apps/web/src/app/exam-authoring/exam-api.service.ts
  apps/web/src/app/exam-authoring/exam-scheduler/exam-scheduler.ts
  apps/web/src/app/exam-authoring/exam-scheduler/exam-scheduler.spec.ts
  apps/web/src/app/exam-authoring/time-zones.ts
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/ScheduleExamHandlerTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/ExamAuthoringFlowTests.cs
DETAILS: Backend:
- ScheduleExamRequest {startLocal, endLocal, lateEntryDeadlineLocal?, timeZone}. timeZone defaults to PlatformTimeZones.Default when omitted.
- The handler:
  1. checks converter.IsKnownZone, otherwise throws UnknownTimeZoneError (400);
  2. converts each local value to UTC;
  3. builds ExamSchedule.Create(..., clock.UtcNow);
  4. calls exam.Schedule(schedule, actor, now);
  5. saves and returns ExamDetailDto, with UTC values plus local echoes.
- Route: PUT /v1/exams/{examId:guid}/schedule, permission exam.manage. This makes exam-api.service.ts:26-32 real.
- Why-comment: the server, not the browser, owns the local-to-UTC mapping, so the chosen zone is the one actually used.

Web (fixes exam-scheduler.ts:121-134, :43):
- Sends the raw datetime-local values plus ':00' as local strings, with the selected timeZone and lateEntryDeadlineLocal. It no longer calls new Date(...).
- Group validators: end > start, and start < lateEntry <= end.
- The select gets id="timeZone", so the label is associated (WCAG 1.3.1).
- Replaces deprecated 'US/Eastern'-style ids with canonical IANA ids in time-zones.ts. Asia/Kolkata stays the default.
- Pre-fills from GET detail (local echoes).
- Shows the server-returned UTC confirmation.
- Uses extractErrorMessage.
TESTS: ScheduleExamHandlerTests.Handle_Kolkata_PassesUtcInstantsToDomain (NSubstitute ITimeZoneConverter + FakeClock)
  ScheduleExamHandlerTests.Handle_UnknownZone_ThrowsBeforeLoadingExam
  ExamAuthoringFlowTests.ScheduleExam_InKolkata_StoresUtcAndEchoesLocal (10:00 IST maps to 04:30Z)
  ExamAuthoringFlowTests.ScheduleExam_EndBeforeStart_Returns400InvalidExamSchedule
  ExamAuthoringFlowTests.ScheduleExam_UnknownTimeZone_Returns400
  ExamAuthoringFlowTests.ScheduleExam_LateEntryAfterEnd_Returns400
  web: exam-scheduler.spec sends {startLocal:'2026-10-05T10:00:00', timeZone:'Asia/Kolkata', lateEntryDeadlineLocal} regardless of browser TZ; invalid when end<=start; label associated

## C12 feat(exam-authoring): publish and archive endpoints under exam.publish (exam status lifecycle, FR-12, FR-13)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/PublishExamCommand.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/PublishExamHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/ArchiveExamCommand.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/ArchiveExamHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringEndpoints.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringModuleInstaller.cs
  apps/api/tests/ExamPlatform.IntegrationTests/ExamAuthoringFlowTests.cs
DETAILS: - POST /v1/exams/{examId:guid}/publish and POST /v1/exams/{examId:guid}/archive, both RequireAuthorization("permission:exam.publish").
- The handlers call PublishAsync / QuestionVersionCatalog.EnsureReferencableAsync(all ids) before Publish. This is the M2 seam: once M2 exists, a retired version blocks publish.
- The 422 ExamNotPublishableError ProblemDetails includes the reasons in 'detail' (joined) so the UI can show them.
TESTS: ExamAuthoringFlowTests.PublishExam_FullyBuiltAndScheduled_Returns204AndDetailShowsPublished
  ExamAuthoringFlowTests.PublishExam_WithoutQuestions_Returns422WithReason
  ExamAuthoringFlowTests.EditPublishedExam_Returns409ExamNotEditable
  ExamAuthoringFlowTests.PublishExam_WithOnlyExamManage_Returns403
  ExamAuthoringFlowTests.ArchiveExam_Published_Returns204

## C13 feat(exam-authoring): ExamAuthoring.Contracts IExamCatalog for cross-module exam lookups (FR-14, D6)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Contracts/ExamPlatform.Modules.ExamAuthoring.Contracts.csproj
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Contracts/IExamCatalog.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Contracts/ExamSummary.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Contracts/ExamLifecycleStatus.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/ExamPlatform.Modules.ExamAuthoring.Application.csproj
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/ExamCatalog.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringModuleInstaller.cs
  apps/api/ExamPlatform.slnx
  apps/api/tests/ExamPlatform.ArchitectureTests/ExamPlatform.ArchitectureTests.csproj
  apps/api/tests/ExamPlatform.ArchitectureTests/ContractsLayerTests.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/ExamCatalogTests.cs
DETAILS: - The Contracts project references SharedKernel.Domain only, following the Consent.Contracts csproj pattern.
- IExamCatalog:
  - GetSummaryAsync(examId)
  - GetSummariesAsync(ids)
  - IsAssignedToBatchAsync(examId, batchId): returns false until the next commit adds assignments.
  - ListAssignedToBatchAsync(batchId)
- ExamSummary(ExamId, Name, ExamLifecycleStatus Status, StartUtc?, EndUtc?, LateEntryDeadlineUtc?, TimeZoneId). ExamLifecycleStatus is a Contracts-owned enum, so the Domain enum is not leaked.
- ExamCatalog (in Application, like ConsentService) sits over IExamReadRepository and is registered in the installer.
- The ContractsLayerTests module list gains ExamAuthoring.
TESTS: ExamCatalogTests.GetSummaryAsync_Unknown_ReturnsNull
  ExamCatalogTests.MapsDomainStatusToContractStatus
  ContractsLayerTests.Contracts_ShouldNotDependOnFrameworksOrOtherProjects(ExamAuthoring.Contracts)

## C14 feat(batch): Batch.Contracts IBatchDirectory for member and batch lookups (FR-14, FR-50a, D6)
FILES: apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Contracts/ExamPlatform.Modules.Batch.Contracts.csproj
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Contracts/IBatchDirectory.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Contracts/BatchSummary.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Contracts/BatchMemberContact.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/ExamPlatform.Modules.Batch.Application.csproj
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Ports/IBatchReadRepository.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/BatchDirectory.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/Repositories/BatchReadRepository.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchModuleInstaller.cs
  apps/api/ExamPlatform.slnx
  apps/api/tests/ExamPlatform.ArchitectureTests/ExamPlatform.ArchitectureTests.csproj
  apps/api/tests/ExamPlatform.ArchitectureTests/ContractsLayerTests.cs
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/BatchDirectoryTests.cs
DETAILS: - IBatchDirectory:
  - GetBatchAsync(batchId) returns BatchSummary(Id, Name, Status, OwnerUserId, MaxMembers, ActiveMemberCount).
  - GetExistingBatchIdsAsync(ids): used for set validation.
  - GetMemberAsync(memberId) returns BatchMemberContact(MemberId, BatchId, Name, Email?, Phone?, IsMinor, OwnerUserId).
  - ListBatchIdsOwnedByAsync(userId)
- All reads are AsNoTracking and exclude soft-deleted rows (the existing query filters).
- OwnerUserId = Batch.CreatedBy (documented: teacher reassignment is out of scope).
TESTS: BatchDirectoryTests.GetMemberAsync_Deleted_ReturnsNull
  BatchDirectoryTests.GetExistingBatchIdsAsync_ReturnsOnlyExisting
  ContractsLayerTests (Batch.Contracts row)

## C15 feat(exam-authoring): assign exams to batches via PUT /v1/exams/{id}/batches (FR-14)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/ExamBatchAssignment.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exam.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Events/ExamBatchAssignmentsChangedEvent.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exceptions/UnknownBatchError.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/ExamPlatform.Modules.ExamAuthoring.Application.csproj
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/SetExamBatchesCommand.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/SetExamBatchesHandler.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/ExamCatalog.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/ExamAuthoringDbContext.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/Repositories/EFExamRepository.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/Repositories/ExamReadRepository.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/Migrations/<timestamp>_M3ExamBatchAssignments.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure/Migrations/ExamAuthoringDbContextModelSnapshot.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringEndpoints.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamRequests.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringModuleInstaller.cs
  apps/api/tests/ExamPlatform.ArchitectureTests/ApplicationLayerTests.cs
  apps/api/tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests/SetExamBatchesHandlerTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/ExamBatchAssignmentFlowTests.cs
DETAILS: Ownership:
- ExamAuthoring owns the assignment. FR-14 says exams are assigned to batches, and it is the exam's delivery configuration.

Model:
- ExamBatchAssignment is a child of the Exam aggregate: Id, ExamId, BatchId, AssignedAtUtc, AssignedByUserId.
- Exam.SetAssignedBatches(IReadOnlyCollection<Guid> batchIds, actor, now) replaces the set and raises ExamBatchAssignmentsChangedEvent(added, removed).
- Allowed in Draft or Published (assigning cohorts after publish is normal). Rejected when Archived.

Handler and route:
- The handler validates the ids through Batch.Contracts IBatchDirectory.GetExistingBatchIdsAsync. Unknown ids throw UnknownBatchError (404, 'batch_not_found').
- ExamAuthoring.Application references Batch.Contracts only.
- Route: PUT /v1/exams/{examId:guid}/batches {batchIds: []}, permission exam.manage.
- EFExamRepository Loaded() also Includes Assignments.
- IExamCatalog.IsAssignedToBatchAsync and ListAssignedToBatchAsync are now real.

Migration:
- New table examAuthoring.ExamBatchAssignments: FK ExamId, cascade; unique (ExamId, BatchId); index BatchId.
- No cross-schema FK to batch.Batches (ADR 0001 accepted trade-off).

Architecture:
- New ApplicationLayerTests fact: ExamAuthoring.Application depends on no other module except *.Contracts.
TESTS: SetExamBatchesHandlerTests.Handle_UnknownBatch_ThrowsUnknownBatchErrorAndDoesNotSave
  ExamAssignmentTests.SetAssignedBatches_WhenArchived_Throws
  ExamAssignmentTests.SetAssignedBatches_RaisesAddedAndRemoved
  ExamBatchAssignmentFlowTests.AssignExamToExistingBatches_ThenGetDetail_ReturnsAssignedBatchIds
  ExamBatchAssignmentFlowTests.AssignExamToUnknownBatch_Returns404
  ExamBatchAssignmentFlowTests.AssignExam_AsInstituteTeacher_Returns403
  ApplicationLayerTests.ExamAuthoringApplication_DependsOnlyOnOtherModulesContracts

## C16 refactor(batch): drop single Batch.ExamId now that exams are assigned to batches (FR-14)
FILES: apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Batch.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Events/BatchCreatedEvent.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Events/BatchActivatedEvent.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Events/BatchClosedEvent.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/CreateBatchCommand.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/CreateBatchHandler.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Dtos/BatchDto.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Ports/IBatchRepository.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/Repositories/EFBatchRepository.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/Migrations/<timestamp>_M3DropBatchExamId.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/Migrations/BatchDbContextModelSnapshot.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchEndpoints.cs
  apps/web/src/app/batch-management/batch-create/batch-create.ts
  apps/web/src/app/batch-management/batch.models.ts
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/CreateBatchHandlerTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/BatchFlowTests.cs
DETAILS: - Removes Batch.ExamId (Batch.cs:11,26-29), the ExamId in batch events, IBatchRepository.ListByExamAsync, and ExamId in CreateBatchRequest/Command/BatchDto.
- This fixes CreateBatchHandler.cs:17 accepting arbitrary exam ids by removing the field.
- Migration M3DropBatchExamId drops the column. Existing links are dev-only data and are not migrated (documented in ADR 0002 and the PR description).
- Web: batch-create removes the free-text Exam ID field (batch-create.ts:18-27,112,133). Exams are assigned from the exam detail page instead.
TESTS: CreateBatchHandlerTests.Handle_ValidRequest_CreatesBatchWithoutExam
  BatchFlowTests.CreateBatch_WithoutExamId_Returns201

## C17 feat(batch): paginated GET /v1/batches, /{id}, /{id}/members with owner scoping and batch.read.all; align web batch models (FR-50, FR-2, Institute/Teacher role)
FILES: apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/IdentitySeeder.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Access/BatchAccessScope.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Queries/ListBatchesQuery.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Queries/ListBatchesHandler.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Queries/GetBatchDetailQuery.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Queries/GetBatchDetailHandler.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Queries/ListBatchMembersQuery.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Queries/ListBatchMembersHandler.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/CreateBatchHandler.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/CreateBatchCommand.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Dtos/BatchDto.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/ExamPlatform.Modules.Batch.Application.csproj
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Ports/IBatchReadRepository.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/Repositories/BatchReadRepository.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchEndpoints.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchAccessScopeFactory.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchModuleInstaller.cs
  apps/web/src/app/batch-management/batch.models.ts
  apps/web/src/app/batch-management/batch-api.service.ts
  apps/web/src/app/batch-management/batch-api.service.spec.ts
  apps/web/src/app/batch-management/batch-list/batch-list.ts
  apps/web/src/app/batch-management/batch-roster/batch-roster.ts
  apps/api/tests/ExamPlatform.IntegrationTests/BatchFlowTests.cs
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/BatchAccessScopeTests.cs
DETAILS: Permission and scope:
- The seeder upsert adds permission 'batch.read.all' (see all batches and invites), granted to SuperAdmin and ExamAdmin. This is a D3 extension, flagged in risks.
- BatchAccessScope(ActorUserId, bool All) has Allows(ownerUserId).
- It is built in Endpoints by BatchAccessScopeFactory from the 'sub' helper and HasClaim('perm','batch.read.all').
- Every Batch query and mutation handler (add member, activate, close, and roster later) takes the scope. An out-of-scope batch throws BatchNotFoundError (404), so batch existence does not leak.

Queries:
- ListBatches: paged, optional status filter.
- GetBatchDetail: BatchDetailDto includes AssignedExams from ExamAuthoring.Contracts IExamCatalog.ListAssignedToBatchAsync (Batch.Application references ExamAuthoring.Contracts).
- ListBatchMembers: paged, BatchMemberDto with Id, Name, Email, Phone, Status, CandidateId, IsMinor.

Routes (fix batch-api.service.ts:18-32):
- GET /v1/batches, GET /v1/batches/{batchId:guid} and GET /v1/batches/{batchId:guid}/members require permission batch.read.
- The existing mutations keep batch.manage from PR2.

Web (fixes batch.models.ts:1-2):
- BatchStatus is 'Pending'|'Active'|'Closed'|'Archived'.
- MemberRegistrationStatus is 'Invited'|'Registered'|'Completed'|'Withdrawn'.
- Services return PagedResult.
- batch-list: pagination; View becomes a detail link (fixes batch-list.ts:110); Activate and Close buttons (makes batch-api.service.ts:34-40 reachable).
- batch-roster:
  - shows member ids and status;
  - load errors are shown via extractErrorMessage instead of console-only (fixes batch-roster.ts:151);
  - the assigned exams list comes from the detail.
TESTS: BatchAccessScopeTests.NonAllScope_RejectsOtherOwner
  BatchFlowTests.ListBatches_AsTeacher_ReturnsOnlyOwnBatches
  BatchFlowTests.ListBatches_AsExamAdmin_ReturnsAll
  BatchFlowTests.GetBatch_OtherTeachersBatch_Returns404
  BatchFlowTests.AddMember_ToOtherTeachersBatch_Returns404
  BatchFlowTests.GetMembers_ReturnsIdsAndBackendStatusNames
  BatchFlowTests.ActivateThenClose_WithMember_Succeeds (regression for the Include fix)
  BatchFlowTests.ListBatches_AsCandidate_Returns403
  web: batch-api.service.spec GET URLs; batch-list shows Activate only for Pending

## C18 feat(batch): roster member fields (name, email-or-phone, minor flag, guardian contact) with normalized, DB-backed uniqueness (FR-50)
FILES: apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/BatchMember.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Batch.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/MemberContact.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/GuardianContact.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Exceptions/InvalidRosterEntryError.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/CreateBatchCommand.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/CreateBatchHandler.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/BatchDbContext.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/Migrations/<timestamp>_M3RosterMemberFields.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/Migrations/BatchDbContextModelSnapshot.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchEndpoints.cs
  apps/web/src/app/batch-management/batch-roster/batch-roster.ts
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/BatchMemberTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/BatchFlowTests.cs
DETAILS: Value objects (Domain):
- MemberContact.Create(email?, phone?):
  - requires at least one value;
  - trims and lowercases email (invariant culture);
  - strips spaces, dashes and parentheses from phone, then requires E.164;
  - throws InvalidRosterEntryError (400) otherwise.
- GuardianContact(Name?, Email?, Phone?) is required when IsMinor. Why-comment: FR-50 and FR-43 minors need a reachable guardian before any consent flow.

BatchMember:
- Adds Name (required, max 200), IsMinor and Guardian*.
- Email becomes nullable.
- Batch.AddMember(name, contact, isMinor, guardian, now) compares against normalized values, which fixes the case-sensitive Batch.cs:42 duplicate check.

Migration M3RosterMemberFields:
- Name defaults to '' for legacy rows.
- Email is dropped to nullable; existing emails are lowercased with an UPDATE.
- Adds IsMinor and Guardian columns.
- Partial unique indexes: (BatchId, Email) WHERE Email IS NOT NULL AND NOT IsDeleted, and (BatchId, Phone) WHERE Phone IS NOT NULL AND NOT IsDeleted.
- The BatchUnitOfWork maps a Postgres 23505 on these indexes to DuplicateMemberError (409), which covers races.

Endpoint and web:
- AddBatchMemberRequest gains name, isMinor and guardian fields.
- The phone length is validated in the domain before the DB (fixes BatchEndpoints.cs:50, where a >20-character phone returned 500).
- Web add-member form gains name, a minor checkbox and guardian fields.
TESTS: BatchMemberTests.Create_EmailUppercase_StoredLowercase
  BatchMemberTests.Create_NoEmailNoPhone_Throws
  BatchMemberTests.Create_MinorWithoutGuardianContact_Throws
  BatchMemberTests.AddMember_DuplicateEmailDifferentCase_ThrowsDuplicateMember
  BatchMemberTests.AddMember_OverCapacity_Throws
  BatchFlowTests.AddMember_DuplicateEmailDifferentCase_Returns409
  BatchFlowTests.AddMember_PhoneTooLong_Returns400NotServerError

## C19 feat(batch): row-level RosterValidator and RFC 4180 CSV parser for roster import (FR-50)
FILES: apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/RosterValidator.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/RosterRow.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/RosterValidationIssue.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Ports/IRosterCsvParser.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Roster/RosterParseResult.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/Roster/RosterCsvParser.cs
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/RosterValidatorTests.cs
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/RosterCsvParserTests.cs
DETAILS: Parser (Infrastructure, no new package):
- Handles quotes, escaped quotes, embedded commas and newlines, CRLF/LF and a UTF-8 BOM.
- Headers are case-insensitive, with aliases:
  - name
  - email
  - phone
  - batch
  - is_minor, also 'minor' (true/false/yes/no/1/0/blank)
  - guardian_name
  - guardian_email
  - guardian_phone
- Output: RosterRow(RowNumber = 1-based file line, fields...) plus file-level issues for a missing required header 'name' or 'email|phone', or an empty file.

RosterValidator (Domain) is reused and reshaped:
- It returns IReadOnlyList<RosterValidationIssue(RowNumber?, Field, Code, Message)> instead of a flat string list.
- Checks:
  - max 1000 rows;
  - name required;
  - email or phone required;
  - email format;
  - E.164 phone;
  - duplicate email or phone within the file (case-insensitive);
  - the batch column, when present, must equal the target batch name (case-insensitive);
  - IsMinor requires guardian email or phone.
- The bare catch at RosterValidator.cs:62 becomes catch (FormatException).
- Messages never echo full email/phone values back into logs, only into the per-row report returned to the authorized caller (NFR-6).
TESTS: RosterCsvParserTests.Parse_QuotedFieldWithComma_KeepsSingleField
  RosterCsvParserTests.Parse_CrLfAndBom_Handled
  RosterCsvParserTests.Parse_HeaderAliasesCaseInsensitive
  RosterCsvParserTests.Parse_MissingNameHeader_ReturnsFileLevelIssue
  RosterCsvParserTests.Parse_RowNumbersMatchFileLines
  RosterValidatorTests.Validate_DuplicateEmailDifferentCase_ReportsBothRows
  RosterValidatorTests.Validate_MinorWithoutGuardian_ReportsGuardianField
  RosterValidatorTests.Validate_BatchColumnMismatch_ReportsRow
  RosterValidatorTests.Validate_InvalidEmail_ReportsFormatIssueNotException
  RosterValidatorTests.Validate_Over1000Rows_ReportsFileLevelIssue

## C20 feat(batch): POST /v1/batches/{id}/roster CSV import with validation report and dry run; roster upload UI (FR-50)
FILES: apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/ImportRosterCommand.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Commands/ImportRosterHandler.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Dtos/RosterImportReportDto.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Events/RosterImportedEvent.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Batch.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchEndpoints.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchModuleInstaller.cs
  apps/web/src/app/batch-management/batch-api.service.ts
  apps/web/src/app/batch-management/batch.models.ts
  apps/web/src/app/batch-management/batch-roster/batch-roster.ts
  apps/web/src/app/batch-management/batch-roster/batch-roster.spec.ts
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/ImportRosterHandlerTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/RosterImportFlowTests.cs
DETAILS: Route:
- POST /v1/batches/{batchId:guid}/roster?dryRun=false
- Body is the raw text/csv. This avoids IFormFile, which forces antiforgery in minimal APIs, and the API is bearer-auth only.
- 1 MB request size limit via RequestSizeLimit metadata.
- Permission batch.manage plus BatchAccessScope.

Handler:
- Order: parse, validate the file, validate against the existing roster (normalized duplicates, capacity MaxMembers), then apply.
- All-or-nothing:
  - any issue returns 422 with RosterImportReportDto {totalRows, importedCount: 0, issues[]};
  - otherwise Batch.ImportMembers(rows, actor, now) adds every member in one SaveChanges and returns 200 with the report;
  - dryRun returns the report without saving.
- Why-comment: partial imports leave admins reconciling half-applied files.
- RosterImportedEvent(BatchId, ActorUserId, Count) carries no PII.

Web:
- batch-roster gets a file input (accept .csv) that posts the File blob with Content-Type text/csv.
- It renders the issues table (row, field, message) with role=alert.
- There is a 'Validate only' button (dryRun), and the member list refreshes on success.
- A downloadable template header line is included.
TESTS: ImportRosterHandlerTests.Handle_AnyRowInvalid_ImportsNothing
  ImportRosterHandlerTests.Handle_DryRun_DoesNotCallSave
  ImportRosterHandlerTests.Handle_RowDuplicatesExistingMember_ReportsRow
  ImportRosterHandlerTests.Handle_ExceedsCapacity_ReportsFileLevelIssue
  RosterImportFlowTests.ImportRoster_ValidCsv_Returns200AndMembersListed
  RosterImportFlowTests.ImportRoster_InvalidRows_Returns422WithRowReportAndImportsNothing
  RosterImportFlowTests.ImportRoster_DryRun_DoesNotPersist
  RosterImportFlowTests.ImportRoster_MinorWithoutGuardian_ReportsRowError
  RosterImportFlowTests.ImportRoster_AsCandidate_Returns403
  RosterImportFlowTests.ImportRoster_OtherTeachersBatch_Returns404
  web: batch-roster.spec posts text/csv and renders issues

## C21 feat(identity): IUserContactLookup in Identity.Contracts for invite recipient binding (FR-50a)
FILES: apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Contracts/IUserContactLookup.cs
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Contracts/UserContact.cs
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/UserContactLookup.cs
  apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/IdentityModuleInstaller.cs
  apps/api/tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests/UserContactLookupTests.cs
DETAILS: - A separate small interface (ISP) next to PR3's age-band contract.
- GetContactAsync(userId) returns UserContact(UserId, Email?, Phone?, DisplayName), or null when the user is missing or not Active.
- The implementation lives in Identity.Application over IUserRepository.
- Why-comment: the JWT deliberately carries no email or phone (PII minimization, NFR-6), so recipient checks go through this lookup.
TESTS: UserContactLookupTests.GetContactAsync_ActiveUser_ReturnsEmailAndPhone
  UserContactLookupTests.GetContactAsync_SuspendedUser_ReturnsNull
  UserContactLookupTests.GetContactAsync_Unknown_ReturnsNull

## C22 fix(invite): CSPRNG hashed single-active codes with status and expiry guards on the injected Clock (FR-50a, section 11 InviteExpiredError)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/InviteCode.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/InviteCodeHash.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/InviteRecipient.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Exceptions/InviteExpiredError.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Exceptions/InviteAlreadyUsedError.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Exceptions/InviteNotPendingError.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Exceptions/InvalidInviteCodeError.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Exceptions/InvalidInviteConfigError.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Events/InviteAcceptedEvent.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Events/InviteCodeIssuedEvent.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Ports/IInviteCodeGenerator.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/CryptoInviteCodeGenerator.cs
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/InviteCodeTests.cs
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/InviteAcceptTests.cs
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/InviteRecipientTests.cs
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/CryptoInviteCodeGeneratorTests.cs
DETAILS: Code generation (fixes Invite.cs:112-120):
- System.Random is removed.
- IInviteCodeGenerator.Generate() returns a 10-character plaintext from the alphabet 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789' using RandomNumberGenerator.GetString. This follows the Identity OtpCodeGenerator.cs pattern.
- Why-comment: 32^10 is about 1.1e15, and ambiguous characters are removed for manual entry.
- InviteCodeHash.Compute(plaintext) upper-cases, then takes the SHA-256 hex, following the LoginSessionIssuer.cs token-hash pattern.

Issuing codes:
- Invite.IssueCode(codeHash, expiresAtUtc, nowUtc):
  - only when Status==Pending, otherwise InviteNotPendingError (409) (fixes Invite.cs:43);
  - expiry window 1..720 hours, otherwise InvalidInviteConfigError (400) (fixes InviteHandlers.cs:52 and InviteEndpoints.cs:61);
  - revokes every other outstanding code, so one active code at a time shrinks the brute-force surface;
  - raises InviteCodeIssuedEvent (no code in the payload).

Recipient and accept:
- InviteRecipient(email?, phone?).Matches(contact) uses the same normalization as the Batch MemberContact rules.
- Invite.AcceptWithCode(codeHash, userId, nowUtc):
  - Accepted: InviteAlreadyUsedError (409).
  - Declined or Revoked: InvalidInviteCodeError (404 'invite_code_invalid', uniform).
  - Code used: InviteAlreadyUsedError.
  - Code revoked: InvalidInviteCodeError.
  - Code expired (now > ExpiresAt): InviteExpiredError (410 'invite_expired').
  - Otherwise it marks the code used and the invite Accepted with AcceptedByUserId.
- InviteAcceptedEvent(InviteId, ExamId, BatchId, BatchMemberId, AcceptedByUserId).
- InviteCode.IsValid, IsExpired, MarkAsUsed and Revoke all take nowUtc (fixes InviteCode.cs:21,33,36) unless PR2 already did this.
- Invite.Accept(Guid inviteCodeId) is removed.
TESTS: InviteCodeTests.IssueCode_WhenRevoked_ThrowsInviteNotPending
  InviteCodeTests.IssueCode_RevokesPreviousOutstandingCode
  InviteCodeTests.IssueCode_ExpiryAbove720Hours_Throws
  InviteCodeTests.IssueCode_ExpiryBelowOneHour_Throws
  InviteAcceptTests.AcceptWithCode_Expired_ThrowsInviteExpired (FakeClock)
  InviteAcceptTests.AcceptWithCode_UsedCode_ThrowsAlreadyUsed
  InviteAcceptTests.AcceptWithCode_RevokedCode_ThrowsInvalidCode
  InviteAcceptTests.AcceptWithCode_Valid_SetsAcceptedByAndRaisesEvent
  InviteRecipientTests.Matches_EmailCaseInsensitive
  InviteRecipientTests.Matches_PhoneIgnoresFormatting
  InviteRecipientTests.Matches_DifferentEmail_False
  CryptoInviteCodeGeneratorTests.Generate_LengthAndAlphabet
  CryptoInviteCodeGeneratorTests.Generate_10000Codes_AllDistinct

## C23 feat(invite): persist code hashes with unique index, xmin concurrency token, BatchId/Phone on Invite (FR-50a, D8)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/InviteCode.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/InviteDbContext.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/InviteRepositoryAndUnitOfWork.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Ports/IInviteRepository.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Exceptions/InviteStateConflictError.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/Migrations/<timestamp>_M3InviteCodeHashing.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/Migrations/InviteDbContextModelSnapshot.cs
DETAILS: Schema:
- InviteCodes.Code (plaintext varchar(8)) becomes CodeHash char(64) with a unique index (fixes InviteDbContext.cs:38).
- The migration Up backfills existing rows with encode(sha256(convert_to(upper("Code"),'UTF8')),'hex') before dropping Code.
- Invites:
  - add BatchId uuid NOT NULL with default '00000000-0000-0000-0000-000000000000' (legacy dev rows) and index (BatchId);
  - add Phone varchar(20) NULL;
  - make Email nullable;
  - add a partial unique index (BatchMemberId, ExamId) WHERE Status='Pending' AND NOT IsDeleted, so there is one pending invite per member per exam;
  - a shadow uint 'Version' property with IsRowVersion(), which Npgsql maps to xmin (no DDL) (fixes InviteDbContext.cs:16).

Repository:
- GetByCodeHashAsync(hash) is tracked, Includes Codes, and joins through the Codes table.
- CodeHashExistsAsync(hash).
- ListByExamAsync and ListByBatchMemberAsync are kept.

Unit of work:
- InviteUnitOfWork.SaveChangesAsync maps DbUpdateConcurrencyException to InviteStateConflictError (409 'invite_state_changed').
- It maps PostgresException 23505 on the pending-invite index to DuplicatePendingInviteError (409).
- Specific catches only. Each is logged with the invite id (section 1.2).
TESTS: Covered by InviteFlowTests/InviteAcceptFlowTests below (concurrency and uniqueness proven against real Postgres)
  dotnet ef migrations has-pending-model-changes --context InviteDbContext reports none

## C24 feat(invite): generate code returns plaintext once with collision retry; web shows code and accept link once (FR-50a)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/GenerateInviteCodeCommand.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/GenerateInviteCodeHandler.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Dtos/InviteDto.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteEndpoints.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteModuleInstaller.cs
  apps/web/src/app/invite-management/invite.models.ts
  apps/web/src/app/invite-management/invite-list/invite-list.ts
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/GenerateInviteCodeHandlerTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/InviteFlowTests.cs
DETAILS: Handler:
- Up to 5 attempts: generate, hash, and check CodeHashExistsAsync until the hash is free.
- Then invite.IssueCode(hash, clock.UtcNow.AddHours(h), clock.UtcNow) and save.
- The unique index is the backstop. If all 5 attempts collide, it throws InviteCodeGenerationFailedError (503, logged).
- Returns IssuedInviteCodeDto(CodeId, Code, ExpiresAtUtc, AcceptPath '/invites/accept?code=...'). This is the only response that ever contains plaintext.

Endpoint:
- Admin routes are constrained to {inviteId:guid}.
- Policy stays invite.manage (from PR2) plus BatchAccessScope (via IBatchDirectory ownership of Invite.BatchId).

Web (fixes invite-list.ts:112):
- Displays the returned code and a copyable absolute accept link in an aria-live panel, with a 'shown once' note.
- Errors via extractErrorMessage (fixes invite-list.ts:116-130).
TESTS: GenerateInviteCodeHandlerTests.Handle_FirstHashTaken_RetriesWithNewCode
  GenerateInviteCodeHandlerTests.Handle_AllAttemptsCollide_ThrowsTypedError
  GenerateInviteCodeHandlerTests.Handle_UsesClockForExpiry
  InviteFlowTests.GenerateCode_ReturnsPlaintextOnce_AndDatabaseStoresOnlyHash (reads InviteDbContext in scope)
  InviteFlowTests.GenerateCode_ForRevokedInvite_Returns409
  InviteFlowTests.GenerateCode_ExpiryHoursZero_Returns400
  InviteFlowTests.GenerateCode_Twice_FirstCodeNoLongerAccepts (checked in accept tests)

## C25 feat(invite): create invites only for roster members and published exams assigned to their batch; web pickers replace free-text GUIDs (FR-14, FR-50a)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/ExamPlatform.Modules.Invite.Application.csproj
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/InviteCommands.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/CreateInviteHandler.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Exceptions/InviteTargetInvalidError.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteEndpoints.cs
  apps/web/src/app/invite-management/invite-create/invite-create.ts
  apps/web/src/app/invite-management/invite-api.service.ts
  apps/web/src/app/invite-management/invite.models.ts
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/CreateInviteHandlerTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/InviteFlowTests.cs
  apps/api/tests/ExamPlatform.ArchitectureTests/ApplicationLayerTests.cs
DETAILS: Request and handler (fixes invite-create.ts:32 and InviteHandlers.cs:12-29):
- CreateInviteRequest shrinks to {batchMemberId, examId}.
- The handler:
  1. IBatchDirectory.GetMemberAsync; if missing or out of scope, BatchMemberNotFound (404);
  2. IExamCatalog.GetSummaryAsync; the exam must be Published, otherwise InviteTargetInvalidError (409, 'exam_not_published');
  3. IExamCatalog.IsAssignedToBatchAsync(examId, member.BatchId), otherwise 409 'exam_not_assigned_to_batch';
  4. copies Email, Phone and BatchId from the roster;
  5. the actor comes from 'sub'.
- Invite.Application references the Batch.Contracts, ExamAuthoring.Contracts and Identity.Contracts projects.
- A new ApplicationLayerTests fact checks Invite.Application depends only on other modules' Contracts.

Web:
- invite-create gets a batch select (GET /v1/batches), then a member select (GET members; shows name and email), then an exam select (batch detail assignedExams, Published only).
- The email field is removed.
TESTS: CreateInviteHandlerTests.Handle_MemberMissing_Throws404
  CreateInviteHandlerTests.Handle_ExamNotPublished_Throws409
  CreateInviteHandlerTests.Handle_ExamNotAssignedToMembersBatch_Throws409
  CreateInviteHandlerTests.Handle_CopiesContactFromRoster
  InviteFlowTests.CreateInvite_ForRosterMember_Returns201WithRosterEmail
  InviteFlowTests.CreateInvite_DuplicatePendingForSameMemberAndExam_Returns409
  ApplicationLayerTests.InviteApplication_DependsOnlyOnOtherModulesContracts

## C26 feat(invite): Enrollment entity and invite-only EnrollmentPolicy (FR-14, section 10 Enrollment, section 11 EnrollmentPolicy)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Enrollment.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Exceptions/AlreadyEnrolledError.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Exceptions/EnrollmentNotAllowedError.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Policies/IEnrollmentPolicy.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Policies/EnrollmentRequest.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Policies/EnrollmentDecision.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Policies/InviteOnlyEnrollmentPolicy.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Ports/IEnrollmentRepository.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/Repositories/EFEnrollmentRepository.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/InviteDbContext.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/Migrations/<timestamp>_M3Enrollments.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/Migrations/InviteDbContextModelSnapshot.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteModuleInstaller.cs
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/InviteOnlyEnrollmentPolicyTests.cs
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/EnrollmentTests.cs
DETAILS: Enrollment aggregate:
- Id, UserId, ExamId, SourceInviteId, EnrolledAtUtc, RevokedAtUtc?, RevokedByUserId?
- Enrollment.Create(userId, examId, inviteId, now) and Revoke(actor, now), which is idempotent-safe: a double revoke throws InviteNotPendingError-style 409.
- Raises EnrollmentCreatedEvent.

IEnrollmentPolicy.EvaluateAsync(EnrollmentRequest(UserId, ExamId, BatchId, SourceInviteId), ct) returns EnrollmentDecision.Allow or Deny(code, message).

InviteOnlyEnrollmentPolicy:
- requires a source invite;
- the exam is Published (IExamCatalog);
- the exam is assigned to the batch;
- EndUtc > clock.UtcNow.
- Why-comment: the section 11 seam; a paid policy later plugs in via DI without touching runtime (OCP).

Migration M3Enrollments:
- Table invite.Enrollments.
- Unique (SourceInviteId).
- Partial unique (UserId, ExamId) WHERE RevokedAtUtc IS NULL.
- The UoW maps 23505 on that index to AlreadyEnrolledError (409).
TESTS: InviteOnlyEnrollmentPolicyTests.Evaluate_ExamNotPublished_Denies
  InviteOnlyEnrollmentPolicyTests.Evaluate_ExamNotAssignedToBatch_Denies
  InviteOnlyEnrollmentPolicyTests.Evaluate_ExamWindowEnded_Denies (FakeClock)
  InviteOnlyEnrollmentPolicyTests.Evaluate_PublishedAssignedOpen_Allows
  EnrollmentTests.Revoke_SetsRevokedAtAndActor
  EnrollmentTests.Revoke_Twice_Throws

## C27 feat(invite): POST /v1/invites/{code}/accept and /decline bound to the caller, atomic enrollment, per-user rate limit; candidate accept page (FR-50a, FR-14, section 13, NFR-5)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/AcceptInviteByCodeCommand.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/AcceptInviteByCodeHandler.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/DeclineInviteByCodeCommand.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/DeclineInviteByCodeHandler.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/InviteCommands.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/InviteHandlers.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Dtos/InviteDto.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteEndpoints.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteRateLimitPolicies.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteModuleInstaller.cs
  apps/api/src/Host/ExamPlatform.Api/appsettings.json
  apps/web/src/app/invite-management/invite-api.service.ts
  apps/web/src/app/invite-management/invite-api.service.spec.ts
  apps/web/src/app/invite-management/invite.models.ts
  apps/web/src/app/invite-management/invite-accept/invite-accept.ts
  apps/web/src/app/invite-management/invite-accept/invite-accept.spec.ts
  apps/web/src/app/app.routes.ts
  apps/api/tests/ExamPlatform.IntegrationTests/InviteAcceptFlowTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/TimeTravelApiFactory.cs
  apps/api/tests/ExamPlatform.IntegrationTests/TestUsers.cs
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/AcceptInviteByCodeHandlerTests.cs
DETAILS: Accept handler, in order:
1. Normalize and hash the code.
2. GetByCodeHashAsync. Missing: InvalidInviteCodeError (404).
3. Look up the caller through Identity.Contracts IUserContactLookup.
4. If !invite.Recipient.Matches(contact), return the same InvalidInviteCodeError. It is logged as a warning with the inviteId and userId only, no PII. Why-comment: uniform errors stop a non-recipient from learning code validity.
5. IEnrollmentPolicy.EvaluateAsync. Deny: EnrollmentNotAllowedError (409, with the reason code).
6. invite.AcceptWithCode(hash, userId, now).
7. enrollments.ExistsActiveAsync. If already enrolled: AlreadyEnrolledError (409).
8. enrollmentRepo.Add(Enrollment.Create(...)).
9. One SaveChanges. The Invite and Enrollment commit atomically; the xmin conflict maps to 409.
- Returns AcceptInviteResultDto(EnrollmentId, ExamId).
- Why-comment: the Enrollment is written in the same transaction as the invite state rather than in a post-commit handler, so a used code can never exist without its enrollment (see risks, D7).

Routes (fixes InviteEndpoints.cs:25,75 and InviteHandlers.cs:60-68):
- POST /v1/invites/{code:regex(^[A-Za-z0-9]{{8,16}}$)}/accept and /decline require authentication only.
- The old /{inviteId}/accept and /{inviteId}/decline routes and the AcceptInviteRequest(InviteCodeId) records are removed.

Rate limit (fixes Program.cs:68 for invites, FR-50a):
- InviteRateLimitPolicies registers the named policy 'invite-accept' via services.Configure<RateLimiterOptions> from the Invite installer, so the Host is untouched (OCP).
- Fixed window, partitioned by the 'sub' claim. PermitLimit and Window come from config 'RateLimiting:InviteAccept' (defaults 10 per 10 minutes).
- The global per-IP limiter still applies.
- If PR1 introduced a rate-limit options helper, reuse it.

Web:
- New /invites/accept route (authGuard; returnUrl keeps ?code=).
- InviteAccept component: code input prefilled from the query; Accept and Decline.
- Maps 404 to 'code not valid for the account you are signed in with', 410 to expired, 409 to already used/enrolled, and 429 to 'too many attempts, wait'.
- On success, navigates to /home.
- acceptInvite(code) and declineInvite(code) service methods replace the GUID versions.

Test support:
- TestUsers.SeedActiveCandidateAsync(factory, email) inserts via IdentityDbContext (the AuthConsentAuditFlowTests.cs:135-145 pattern) and returns the id and a token.
- TimeTravelApiFactory : ApiFactory replaces Clock with a mutable FakeClock through ConfigureTestServices.
TESTS: AcceptInviteByCodeHandlerTests.Handle_RecipientMismatch_ThrowsInvalidCodeBeforeRevealingState
  AcceptInviteByCodeHandlerTests.Handle_PolicyDenies_DoesNotMutateInvite
  AcceptInviteByCodeHandlerTests.Handle_AlreadyEnrolled_Throws
  AcceptInviteByCodeHandlerTests.Handle_Success_AddsEnrollmentAndSavesOnce
  InviteAcceptFlowTests.AcceptByCode_ByInvitee_Returns200AndCreatesEnrollment
  InviteAcceptFlowTests.AcceptByCode_ByDifferentUser_Returns404AndCodeStaysUsableForInvitee
  InviteAcceptFlowTests.AcceptByCode_Twice_SecondReturns409
  InviteAcceptFlowTests.AcceptByCode_TwoConcurrentRequests_ExactlyOneSucceedsAndOneEnrollmentRow
  InviteAcceptFlowTests.AcceptByCode_AfterRegenerate_OldCodeReturns404
  InviteAcceptFlowTests.AcceptByCode_ExpiredCode_Returns410 (TimeTravelApiFactory)
  InviteAcceptFlowTests.AcceptByCode_ExamArchived_Returns409EnrollmentNotAllowed
  InviteAcceptFlowTests.AcceptByCode_ExceedsPerUserLimit_Returns429
  InviteAcceptFlowTests.AcceptByCode_Unauthenticated_Returns401
  InviteAcceptFlowTests.DeclineByCode_ByInvitee_SetsDeclined
  web: invite-accept.spec reads ?code=, posts /v1/invites/{code}/accept, maps 410/429 messages

## C28 feat(invite): revoke semantics, including revoking an accepted invite's enrollment (FR-50a, FR-14)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Events/InviteRevokedEvent.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/InviteHandlers.cs
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/InviteRevokeTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/InviteAcceptFlowTests.cs
DETAILS: Invite.Revoke(actor, now):
- Pending: revokes every outstanding code and sets Status to Revoked.
- Accepted: sets Status to Revoked with WasAccepted=true. RevokeInviteHandler then loads the Enrollment by SourceInviteId and calls Enrollment.Revoke(actor, now) in the same SaveChanges.
- Declined, Revoked or Expired: InviteNotPendingError (409). This replaces InvalidOperationException (Invite.cs:95-104).
- InviteRevokedEvent(InviteId, ExamId, BatchMemberId, WasAccepted, ActorUserId).
- Why-comment: invite-only access must be withdrawable before the exam window. M4 attempts check IEnrollmentQuery, which ignores revoked enrollments.
TESTS: InviteRevokeTests.Revoke_Pending_RevokesAllOutstandingCodes (regression for the missing Include)
  InviteRevokeTests.Revoke_Declined_ThrowsInviteNotPending
  InviteRevokeTests.Revoke_Accepted_FlagsWasAccepted
  InviteAcceptFlowTests.RevokeAcceptedInvite_RevokesEnrollment_AndMyEnrollmentsNoLongerListsIt

## C29 feat(invite): paginated GET /v1/invites and /{id} with scoping and effective Expired status; fix web invite list (FR-50a, FR-2)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Queries/ListInvitesQuery.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Queries/ListInvitesHandler.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Queries/GetInviteQuery.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Queries/GetInviteHandler.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Ports/IInviteReadRepository.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Dtos/InviteDto.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/Repositories/InviteReadRepository.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteEndpoints.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteAccessScopeFactory.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteModuleInstaller.cs
  apps/web/src/app/invite-management/invite.models.ts
  apps/web/src/app/invite-management/invite-api.service.ts
  apps/web/src/app/invite-management/invite-list/invite-list.ts
  apps/web/src/app/invite-management/invite-list/invite-list.spec.ts
  apps/api/tests/ExamPlatform.IntegrationTests/InviteFlowTests.cs
DETAILS: Queries and routes (fixes invite-api.service.ts:18-24):
- GET /v1/invites?examId=&batchId=&status=&page=&pageSize= and GET /v1/invites/{inviteId:guid}, both permission invite.manage.
- Scope: callers without batch.read.all only see invites whose BatchId is in IBatchDirectory.ListBatchIdsOwnedByAsync(caller). An out-of-scope detail returns 404.
- InviteDto:
  - EffectiveStatus is computed. Why-comment: Pending with codes, every one of them revoked or expired, reports Expired, so the enum's Expired value is meaningful without a background job.
  - Codes[] carries metadata only: id, expiresAtUtc, usedAtUtc, revokedAtUtc. It never includes the code or hash.
  - Also includes acceptedByUserId and batchId.

Web (fixes invite-list.ts:50 and invite.models.ts:1):
- The 'Sent' status is removed.
- Actions are gated on 'Pending': Generate Code and Revoke. Revoke is also available on 'Accepted', with a confirm that says it removes the enrollment.
- Filters and pagination are added.
TESTS: InviteFlowTests.ListInvites_AsTeacher_ReturnsOnlyInvitesForOwnBatches
  InviteFlowTests.ListInvites_NeverSerializesCodeOrHash
  InviteFlowTests.GetInvite_AllCodesExpired_ReportsEffectiveStatusExpired (TimeTravelApiFactory)
  InviteFlowTests.ListInvites_AsCandidate_Returns403
  web: invite-list.spec shows Generate/Revoke for Pending, hides for Declined, displays issued code

## C30 feat(invite): Invite.Contracts IEnrollmentQuery and integration events; GET /v1/me/enrollments and a minimal candidate landing page (FR-14, FR-16 prerequisite)
FILES: apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Contracts/ExamPlatform.Modules.Invite.Contracts.csproj
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Contracts/IEnrollmentQuery.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Contracts/EnrollmentSummary.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Contracts/InviteIntegrationEvents.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/ExamPlatform.Modules.Invite.Application.csproj
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/EnrollmentQuery.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Events/PublishInviteIntegrationEvents.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Queries/ListMyEnrollmentsQuery.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Queries/ListMyEnrollmentsHandler.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteEndpoints.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteModuleInstaller.cs
  apps/api/ExamPlatform.slnx
  apps/api/tests/ExamPlatform.ArchitectureTests/ExamPlatform.ArchitectureTests.csproj
  apps/api/tests/ExamPlatform.ArchitectureTests/ContractsLayerTests.cs
  apps/web/src/app/candidate/enrollment-api.service.ts
  apps/web/src/app/candidate/enrollment.models.ts
  apps/web/src/app/candidate/candidate-home/candidate-home.ts
  apps/web/src/app/candidate/candidate-home/candidate-home.spec.ts
  apps/web/src/app/app.routes.ts
  apps/web/src/app/app.html
  apps/api/tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests/EnrollmentQueryTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/InviteAcceptFlowTests.cs
DETAILS: Contracts and integration events:
- IEnrollmentQuery.IsEnrolledAsync(userId, examId) and ListActiveForUserAsync(userId). This is the M4 seam: attempt start checks it together with IConsentService.
- Integration events are records deriving from SharedKernel.Domain DomainEvent:
  - InviteAcceptedIntegrationEvent(InviteId, ExamId, BatchId, BatchMemberId, UserId)
  - InviteRevokedIntegrationEvent(InviteId, BatchMemberId, WasAccepted)
- PublishInviteIntegrationEvents is an IDomainEventHandler for the domain events. It re-dispatches through IDomainEventDispatcher, so other modules depend only on Invite.Contracts.
- If PR2 or PR3 established a different integration-event convention, follow it.

Endpoint:
- GET /v1/me/enrollments requires authentication and is always scoped to 'sub'.
- It returns EnrollmentDto(EnrollmentId, ExamId, ExamName, ExamStatus, StartUtc, EndUtc, LateEntryDeadlineUtc, TimeZone, EnrolledAtUtc), composed with IExamCatalog.GetSummariesAsync.

Web:
- /home (authGuard) CandidateHome lists enrollments. Times are formatted with Intl.DateTimeFormat in the exam's timeZone.
- It links to /invites/accept.
- Explicitly not the FR-16 upcoming/ongoing/completed dashboard.
TESTS: EnrollmentQueryTests.IsEnrolled_RevokedEnrollment_False
  InviteAcceptFlowTests.MyEnrollments_ReturnsOnlyCallersActiveEnrollmentsWithExamSummary
  ContractsLayerTests (Invite.Contracts row)
  web: candidate-home.spec renders exam name and time in Asia/Kolkata

## C31 feat(batch): mark BatchMember registered or withdrawn from invite integration events (FR-50, FR-14)
FILES: apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/ExamPlatform.Modules.Batch.Application.csproj
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Events/MarkBatchMemberRegisteredOnInviteAccepted.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Events/WithdrawBatchMemberOnAcceptedInviteRevoked.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Ports/IBatchRepository.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Batch.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/BatchMember.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Infrastructure/Repositories/EFBatchRepository.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchModuleInstaller.cs
  apps/api/tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests/InviteEventHandlersTests.cs
  apps/api/tests/ExamPlatform.IntegrationTests/InviteAcceptFlowTests.cs
  apps/api/tests/ExamPlatform.ArchitectureTests/ApplicationLayerTests.cs
DETAILS: - Batch.Application references Invite.Contracts.
- The handlers load the batch by member id with GetByMemberIdAsync, which includes Members.
- They call Batch.MarkMemberRegistered(memberId, userId, now), which fixes BatchMember.MarkRegistrationCompleted (BatchMember.cs:35) never being called, or Batch.WithdrawMember.
- They are idempotent: an already Registered member with the same candidate is a no-op, and a missing member is logged as an error and rethrown per the dispatcher policy (section 1.2).
- Why-comment: this is a post-commit projection in another module's schema, so it cannot share Invite's transaction.
TESTS: InviteEventHandlersTests.Accepted_MarksMemberRegisteredWithCandidateId
  InviteEventHandlersTests.Accepted_Twice_IsNoOp
  InviteEventHandlersTests.RevokedAccepted_WithdrawsMember
  InviteEventHandlersTests.RevokedPending_DoesNothing
  InviteAcceptFlowTests.AcceptByCode_MarksBatchMemberRegistered_VisibleInGetMembers
  ApplicationLayerTests.BatchApplication_DependsOnlyOnOtherModulesContracts

## C32 feat(audit): audit M3 scheduling, publishing, assignment, roster import, invite code and enrollment events (FR-40)
FILES: apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Events/ExamAuditHandlers.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Application/Events/BatchAuditHandlers.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Events/InviteAuditHandlers.cs
  apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringModuleInstaller.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchModuleInstaller.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteModuleInstaller.cs
  apps/api/tests/ExamPlatform.IntegrationTests/M3AuditFlowTests.cs
DETAILS: - Extends PR2's audit handler pattern, using Admin.Contracts IAuditLogger. If PR2 put the handlers in these same files, append to them.
- Events handled:
  - ExamConfigUpdated
  - ExamScheduled
  - ExamPublished
  - ExamArchived
  - ExamBatchAssignmentsChanged
  - RosterImported (count only)
  - InviteCodeIssued (no code)
  - InviteAccepted
  - InviteRevoked (WasAccepted)
  - EnrollmentCreated
- Metadata carries ids only, no email or phone (NFR-6).
TESTS: M3AuditFlowTests.PublishExam_WritesExamPublishedAuditEntryWithActor
  M3AuditFlowTests.AcceptInvite_WritesInviteAcceptedAndEnrollmentCreatedEntries
  M3AuditFlowTests.RosterImport_AuditMetadataContainsNoEmails

## C33 feat(web): exam detail page for config, sections, question references, batch assignment and publish; builder hands off to it (FR-11, FR-12, FR-14, FR-2)
FILES: apps/web/src/app/exam-authoring/exam-detail/exam-detail.ts
  apps/web/src/app/exam-authoring/exam-detail/exam-detail.spec.ts
  apps/web/src/app/exam-authoring/exam-detail/exam-config-form.ts
  apps/web/src/app/exam-authoring/exam-api.service.ts
  apps/web/src/app/exam-authoring/exam-api.service.spec.ts
  apps/web/src/app/exam-authoring/exam-builder/exam-builder.ts
  apps/web/src/app/exam-authoring/exam.models.ts
  apps/web/src/app/app.routes.ts
DETAILS: Route:
- exams/:id, guarded by exam.manage.

Config form, which fixes exam-builder.ts:136-161 dropping fields and the minutes/seconds label mismatch:
- total time in minutes, converted to seconds on submit;
- shuffle questions and options;
- section lock;
- calculator and scratchpad;
- max attempts and max retakes;
- result release mode (Instant/Scheduled/Manual), plus a local release time in the exam zone when Scheduled;
- marking scheme: correct marks, incorrect marks (<= 0 with a hint) and unattempted marks.

Sections and questions:
- Add, rename or set the timer, and remove sections.
- Add a question by QuestionVersionId, with a GUID validator and an 'M2 will provide a picker' hint, plus optional marks. Remove a question.

Other panels:
- Batch assignment is a multi-select from GET /v1/batches, saved with PUT /v1/exams/{id}/batches.
- A Schedule link.
- Publish and Archive buttons, shown only if hasPermission('exam.publish'). A 422 shows the reasons list.
- All errors via extractErrorMessage with role=alert.

Builder:
- Keeps name, description and seriesId only.
- On success, navigates to /exams/{id}.
TESTS: exam-detail.spec: config submit converts 90 minutes -> totalTimeSeconds 5400 and sends markingScheme object
  exam-detail.spec: Publish hidden without exam.publish
  exam-detail.spec: 422 reasons rendered
  exam-api.service.spec: PUT config/schedule/batches, POST sections/questions/publish URLs

## C34 docs: correct FR labels and completion claims; align dev API port and production apiBaseUrl; ADR 0002 for M3 ownership decisions (FR-50, FR-50a, FR-14, FR-13)
FILES: README.md
  docs/adr/0002-m3-enrollment-scheduling-ownership.md
  apps/web/src/environments/environment.development.ts
  apps/web/src/environments/environment.ts
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchEndpoints.cs
  apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Batch.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteEndpoints.cs
  apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs
DETAILS: Web environments:
- environment.development.ts:2 becomes 'http://localhost:5239', which matches the launchSettings.json 'http' profile.
- environment.ts:2 becomes '' (same-origin, since the API serves /v1 at the root), with a comment that production must serve the SPA and API on one origin or override this at build.

README:
- The run command drops '--urls http://localhost:5080' in favour of '--launch-profile http'; the health, Scalar and OpenAPI URLs are updated.
- Module FR labels are fixed:
  - Batch: FR-17..19 becomes FR-50 / FR-14.
  - Invite: FR-20..21 becomes FR-50a / FR-14.
  - Guardian: FR-22..24 becomes FR-45 (coordinate with PR3).
- The API list at README.md:395-402 is fixed: register/login/otp are FR-1, and the new M3 routes are added.
- The 'âœ… M1 & M3 Complete' claims (README.md:48,138-146,486) become an accurate status: M3 manual authoring, scheduling, batches, CSV roster and invite lifecycle are done. Rule-based generation (FR-11, blocked on M2), preview (FR-15), accommodations (FR-49), proctoring profiles and notifications are pending.

Source headers:
- Fixes mislabeled FR IDs in the source headers (BatchEndpoints.cs:8, Batch.cs:7, InviteEndpoints.cs:8, Invite.cs:7) if PR2 has not already.

ADR 0002 (using docs/adr/template.md) records:
- ExamBatchAssignment ownership and the Batch.ExamId drop;
- Enrollment and EnrollmentPolicy in the Invite module;
- in-transaction enrollment vs. post-commit cross-module events;
- the integration events in Contracts;
- NodaTime vs TimeZoneInfo under InvariantGlobalization;
- local wall-clock scheduling input;
- hashed invite codes;
- the batch.read.all scoping;
- the M2 question-version seam and FR-11 plan;
- the proctoring-profile (section 8) seam.
TESTS: Manual: dotnet run --launch-profile http and npm start; SPA health widget reports healthy
  npm run build (production) succeeds with apiBaseUrl ''

## migrations
- ExamAuthoring / ExamAuthoringDbContext: M3ExamAuthoringCompletion. Adds ExamQuestions.Marks numeric(6,2) NULL and a unique index ExamQuestions(SectionId, QuestionVersionId). Sets MarkingScheme precision. Makes the schedule columns nullable and nulls out MinValue placeholders, but only if PR2 has not done this.
- ExamAuthoring / ExamAuthoringDbContext: M3ExamBatchAssignments. Creates table examAuthoring.ExamBatchAssignments (Id, ExamId FK cascade, BatchId, AssignedAtUtc, AssignedByUserId), with a unique index (ExamId, BatchId) and an index on BatchId. There is no cross-schema FK.
- Batch / BatchDbContext: M3DropBatchExamId. Drops batch.Batches.ExamId. Existing links are dev-only and are not migrated.
- Batch / BatchDbContext: M3RosterMemberFields. Adds BatchMembers.Name (varchar(200), default ''), IsMinor bool (default false), GuardianName, GuardianEmail and GuardianPhone. Makes Email nullable and lowercases existing emails. Adds partial unique indexes on (BatchId, Email) and (BatchId, Phone), both WHERE the value is not null AND NOT IsDeleted.
- Invite / InviteDbContext: M3InviteCodeHashing. Adds InviteCodes.CodeHash char(64), backfilled with SQL encode(sha256(convert_to(upper(Code),'UTF8')),'hex'), then drops Code and adds a unique index on CodeHash. Adds Invites.BatchId uuid NOT NULL (default empty GUID for legacy rows) with an index, and Invites.Phone varchar(20) NULL. Makes Email nullable. Adds a partial unique index (BatchMemberId, ExamId) WHERE Status='Pending' AND NOT IsDeleted. The xmin row version needs no DDL. If PR2 has not already dropped the junk Invites.CreatedBy timestamptz column, drop it here.
- Invite / InviteDbContext: M3Enrollments. Creates table invite.Enrollments (Id, UserId, ExamId, SourceInviteId, EnrolledAtUtc, RevokedAtUtc NULL, RevokedByUserId NULL), with a unique index on SourceInviteId and a partial unique index (UserId, ExamId) WHERE RevokedAtUtc IS NULL.
- Identity: no schema change. The seeder upsert adds the permission batch.read.all and grants it to SuperAdmin and ExamAdmin.
- Generate each migration from apps/api with: dotnet ef migrations add <Name> --project src/Modules/<M>/ExamPlatform.Modules.<M>.Infrastructure --startup-project src/Host/ExamPlatform.Api --context <M>DbContext --output-dir Migrations. For ExamAuthoring use --context ExamAuthoringDbContext. After adding each one, run dotnet ef migrations has-pending-model-changes.

## reuse
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/Authorization/PermissionPolicyProvider.cs and PermissionAuthorizationHandler.cs: RequireAuthorization("permission:<code>") for every new route (example: apps/api/src/Modules/Admin/ExamPlatform.Modules.Admin.Endpoints/AdminEndpoints.cs:24).
- PR2's shared JWT 'sub' helper (D2), relocated from apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Endpoints/ClaimsPrincipalExtensions.cs and ConsentEndpoints.cs CallerId. Add a HasPermission(code) sibling there for the scope factories.
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/IdentitySeeder.cs: PR2's idempotent upsert, so adding batch.read.all is one entry.
- apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Domain/DomainException.cs plus apps/api/src/Host/ExamPlatform.Api/DomainExceptionHandler.cs: every new typed error maps automatically, with no Host edit.
- apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/Clock.cs, and FakeClock from apps/api/tests/Modules/Consent/ExamPlatform.Modules.Consent.UnitTests/FakeClock.cs (copy it into each unit test project, or into a shared test helper).
- apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/IDomainEventDispatcher.cs (IDomainEventHandler<T>), SharedKernel.Infrastructure/InProcessDomainEventDispatcher.cs and DomainEventsSaveChangesInterceptor.cs: integration events and audit handlers.
- apps/api/src/Modules/Consent/ExamPlatform.Modules.Consent.Contracts/*.csproj and IConsentService.cs: template for the new ExamAuthoring, Batch and Invite Contracts projects (reference SharedKernel.Domain only; implement in Application).
- apps/api/src/Modules/Admin/ExamPlatform.Modules.Admin.Contracts/IAuditLogger.cs: audit handlers.
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/OtpCodeGenerator.cs (RandomNumberGenerator) and apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Application/LoginSessionIssuer.cs:30-33 (random token plus stored hash): invite code generation and hashing.
- apps/api/src/Modules/Identity/ExamPlatform.Modules.Identity.Infrastructure/Repositories/UserRepository.cs Loaded() Include pattern: the new GetByCodeHashAsync and GetByMemberIdAsync.
- apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/RosterValidator.cs: reshaped into a row-level validator instead of rewritten.
- apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/ExamConfig.cs, MarkingScheme.cs, ResultReleaseMode.cs, ExamSection.TimeSeconds and the existing Exam schedule columns: FR-12/FR-13 need no new config entity.
- apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Dtos/SectionDto.cs AddSectionRequest (previously unused).
- apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Application/IModuleInstaller.cs: per-module handler, contract and rate-limit-policy registration, with the Host unchanged.
- apps/api/tests/ExamPlatform.IntegrationTests/ApiFactory.cs, TestJwtTokenBuilder.cs (PR2 perm variant), CapturingOtpSender.cs, and the user-seeding pattern in AuthConsentAuditFlowTests.cs:135-145 (User.Register through IdentityDbContext).
- apps/web/src/app/shared/problem-details.ts extractErrorMessage; apps/web/src/app/auth/auth.guard.ts as the template for permission.guard.ts; apps/web/src/app/auth/testing/fake-jwt.ts buildFakeJwt for guard and nav specs; apps/web/src/styles.css shared classes; the signal-based component style from apps/web/src/app/auth/login/login.ts.

## covers
- apps/web/src/app/exam-authoring/exam-api.service.ts:18-32: GET /v1/exams, GET /v1/exams/{id} and PUT /v1/exams/{id}/schedule do not exist, so the exam list and scheduler always fail.
- apps/web/src/app/batch-management/batch-api.service.ts:18-32: GET /v1/batches, /{id} and /{id}/members do not exist, so the batch list and roster fail.
- apps/web/src/app/invite-management/invite-api.service.ts:18-24: GET /v1/invites and /{id} do not exist, so the invite list fails.
- apps/web/src/app/exam-authoring/exam-builder/exam-builder.ts:160, together with ExamAuthoringEndpoints.cs:40: seriesId '' fails to bind to a non-nullable Guid, returning 400.
- apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/CreateExamCommand.cs:8: non-nullable SeriesId persists Guid.Empty.
- apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Dtos/CreateExamRequest.cs:4: dead duplicate DTO.
- apps/web/src/app/exam-authoring/exam-builder/exam-builder.ts:136-161: FR-12 config fields are collected but dropped; 'minutes' label is bound to seconds.
- apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exam.cs:47-69 and ExamSection.cs:29-43: UpdateConfig, sections and questions are unreachable and have no Draft-only guard; RemoveSection and RemoveQuestion silently no-op.
- apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exam.cs:14: public setters bypass the lifecycle (unless PR2 already fixed it).
- apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Domain/Exam.cs:73-84: Publish does not check schedule, questions or config; InvalidOperationException gives a 500; there is no Archive.
- apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/Commands/CreateExamHandler.cs:20-21: DateTime.MinValue schedule placeholders; no schedule command, invariants or IANA validation (FR-13).
- apps/web/src/app/exam-authoring/exam-scheduler/exam-scheduler.ts:121-134: browser time zone is used instead of the selected zone; lateEntryDeadline is dropped; no end > start validation.
- apps/web/src/app/exam-authoring/exam-scheduler/exam-scheduler.ts:43: the time-zone label is not associated with its select (WCAG).
- apps/api/Directory.Build.props:10: InvariantGlobalization=true makes IANA TimeZoneInfo lookup fail on Windows. Resolved with NodaTime, and verified by a Windows CI job.
- apps/web/src/app/exam-authoring/exam.models.ts:1-3,41-59: ExamStatus Active/Closed, ResultReleaseMode 'Automatic', MarkingScheme as a string, and Section/Question DTO shape drift.
- apps/web/src/app/batch-management/batch.models.ts:1-2: BatchStatus 'Draft' vs Pending; MemberRegistrationStatus drift.
- apps/web/src/app/invite-management/invite.models.ts:1 and invite-list.ts:50: the nonexistent 'Sent' status hides the Generate Code and Revoke actions.
- apps/web/src/app/invite-management/invite-list/invite-list.ts:112-130: the generated code is discarded; errors are console-only.
- apps/web/src/app/batch-management/batch-roster/batch-roster.ts:151: a load error only logs to the console and shows 'No members'.
- apps/web/src/app/exam-authoring/exam-list/exam-list.ts:100 and batch-list.ts:110: the View buttons are no-ops.
- apps/web/src/app/batch-management/batch-api.service.ts:34-40: activate and close are unreachable from the UI.
- apps/web/src/app/invite-management/invite-create/invite-create.ts:32 and InviteHandlers.cs:12-29: free-text BatchMember GUID; member existence and email are never checked.
- apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Endpoints/InviteEndpoints.cs:25,75: accept uses InviteCodeId (a GUID) instead of POST /v1/invites/{code}/accept (section 13).
- apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/InviteHandlers.cs:60-68: accept is not bound to the caller, creates no Enrollment, and does not update the BatchMember.
- apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs:112-120: System.Random codes; no uniqueness check; plaintext storage.
- apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/InviteDbContext.cs:38: no unique index on InviteCodes.Code.
- apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Infrastructure/InviteDbContext.cs:16: no concurrency token, so a single-use code can be double-accepted.
- apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs:43: GenerateCode has no status guard; Invite.cs:95-104 revoking an Accepted invite has undefined semantics.
- apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Application/Commands/InviteHandlers.cs:52 and InviteEndpoints.cs:61: ExpiryHours unvalidated (negative values or a 500 from AddHours).
- apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/InviteStatus.cs:8: Expired is never set; InviteExpiredError is missing (section 11).
- apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/InviteCode.cs:21,33,36: expiry uses DateTime.UtcNow instead of Clock (unless PR2 already fixed it).
- apps/api/src/Host/ExamPlatform.Api/Program.cs:63-73: no named rate limit on invite accept (brute force, FR-50a).
- apps/api/src/Modules/Invite/ExamPlatform.Modules.Invite.Domain/Invite.cs:79: InviteAcceptedEvent has no handler.
- apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/BatchMember.cs:35: MarkRegistrationCompleted is never called.
- Repo-wide: no Enrollment entity, no EnrollmentPolicy, and no Invite.Contracts enrollment query (FR-14; sections 10 and 11).
- apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/RosterValidator.cs:4-77: unused; flat error strings; RosterValidator.cs:62 bare catch; no CSV import endpoint or UI (FR-50).
- apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/BatchMember.cs:3-16: no Name, guardian contact or minor flag (FR-50).
- apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Batch.cs:42: case-sensitive duplicate email; BatchDbContext.cs:42-57 has no unique index on (BatchId, Email).
- apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchEndpoints.cs:50: no input validation; a phone longer than 20 characters fails at the DB and returns 500.
- apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Domain/Batch.cs:11: a single ExamId per batch (FR-14 modeled backwards); CreateBatchHandler.cs:17 accepts any ExamId.
- ROLE-INSTITUTE-TEACHER: no per-batch ownership scoping for teachers, for batches or invites.
- apps/web/src/app/auth/jwt.ts:2-6: perm and role claims are ignored.
- apps/web/src/app/app.routes.ts:30-89: staff routes are guarded only by authGuard.
- apps/web/src/app/app.html:3-12: nav is not role-aware.
- apps/web/src/app/auth/login/login.ts:89 and verify-otp.ts:51: the default landing is /profile, not a role landing.
- FR-16 gap: no candidate invite-accept UI and no post-login candidate landing (a minimal enrollments list is added; the full dashboard stays out of scope).
- apps/web/src/environments/environment.development.ts:2: port 5080 does not match launchSettings 5239/7034.
- apps/web/src/environments/environment.ts:2: '/api' base URL with no proxy or path base.
- README.md:48,99-105,138-146,203-214,395-402,486: mislabeled FR IDs, 'M1/M3 complete' claims, wrong port.
- apps/api/src/Modules/Batch/ExamPlatform.Modules.Batch.Endpoints/BatchEndpoints.cs:8, Batch.cs:7, InviteEndpoints.cs:8 and Invite.cs:7: wrong FR IDs in code headers (unless PR2 already fixed them).
- apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Endpoints/ExamAuthoringEndpoints.cs:34: Created Location headers pointed at nonexistent GET routes.
- apps/web/src/app/exam-authoring/exam-api.service.spec.ts:36-50 (and the batch and invite specs): assert nonexistent endpoints; use the deprecated HttpClientTestingModule.
- apps/api/tests/ExamPlatform.ArchitectureTests/ContractsLayerTests.cs:26: the new ExamAuthoring, Batch and Invite Contracts projects are added to the boundary checks.
- Integration-test gap (audit SEC14): accept, activate, revoke and roster flows were never exercised, which hid the Include bugs. Now covered end to end.

## risks
- Baseline drift: this plan assumes PR2/PR3 deliverables, including handler shape, helper location and name, the seeder upsert, typed errors, Include fixes, Clock params, nullable schedule, the perm-claim test builder, the audit handler pattern and Identity.Contracts. Before starting, diff PR3's head against the dependsOn list. Commits 5-7 and 21 each contain 'unless PR2 did it' sub-steps that must be skipped, not duplicated.
- D7 deviation (flagged): Enrollment is written in the same transaction as the invite accept, not by a post-commit IDomainEventHandler<InviteAcceptedEvent>. The dispatcher runs in SavedChangesAsync after commit, so a handler failure would leave a used code with no enrollment. A nested SaveChanges on the same DbContext inside the interceptor is also fragile. Cross-module effects (BatchMember registration, audit) still use IDomainEventHandler, per D7.
- Post-commit cross-module projection: if the Batch handler fails after the invite commits, the dispatcher rethrows and the client gets 500 even though the enrollment exists. The handlers are idempotent, and a reconciliation or outbox is future work (ADR 0001 defers durable messaging). The UI should treat a 500 on accept by re-checking /v1/me/enrollments.
- D3 extension (flagged): adds the permission batch.read.all, granted to SuperAdmin and ExamAdmin, for 'see and mutate all batches and invites'. Without it, teacher scoping would have to overload exam.manage. Reviewers may prefer a different code name.
- New dependency: NodaTime in SharedKernel.Infrastructure, recorded in ADR 0002. The alternative is setting InvariantGlobalization=false, which would require ICU (libicu) in Linux containers and makes behaviour OS-dependent. Without either, Asia/Kolkata throws on Windows dev machines.
- Breaking schema and data changes: dropping Batch.ExamId and replacing plaintext invite codes with hashes. The migrations only run in Development (Program.cs:126) and no production data exists, but existing dev batch-to-exam links are lost, and previously issued codes keep working only via the SQL backfill.
- API contract breaks: list endpoints are now PagedResult; the accept and decline routes change from /{inviteId}/accept to /{code}/accept; CreateBatch and CreateInvite request shapes shrink. The web is updated in the same PR (D10). There are no other clients.
- Hashed codes cannot be re-displayed. An admin who loses a code must regenerate it, which revokes the previous one. The UI states this.
- Recipient matching depends on email and phone normalization being consistent between Identity (registration) and the roster. A phone stored without the country code in Identity would not match an E.164 roster phone; email matching mitigates this. Identity-side E.164 normalization is a follow-up.
- The uniform 404 for a non-recipient can confuse candidates signed in with the wrong account. This is mitigated by explicit UI copy.
- Rate limiting is in-memory and per instance (NFR-2); a Redis-backed limiter comes later. Integration tests must use a fresh user per test so the per-user 'invite-accept' partition does not bleed across tests.
- The concurrency test (two simultaneous accepts) depends on the Npgsql xmin row-version mapping of a shadow property on an aggregate using the 'public new Guid Id => base.Id' pattern (the critic's unverified risk). Implement and run it early. If shadow xmin misbehaves, use an explicit uint Version property.
- Minimal APIs force antiforgery on IFormFile, so roster upload takes a raw text/csv body. A multipart upload later would need DisableAntiforgery, with a why-comment that bearer tokens mean no CSRF.
- Local wall-clock scheduling with strict DST resolution rejects ambiguous and skipped local times in DST zones. This is intentional; Asia/Kolkata has no DST.
- Scope: PR4 is large (about 31 commits). It is ordered so it can be split at the commit 14/15 boundary into PR4a (exam authoring, FR-12/13, plus web RBAC) and PR4b (batch, roster, invites, enrollment) if reviewers ask.
- Commit attribution: requirements section 1.1 and D10 forbid Co-authored-by and AI trailers. The user's project rule overrides the harness attribution reminder, so no trailer is added.

## outOfScope
- FR-11 rule-based paper generation, and any validation of QuestionVersionId against a Question Bank. M2 does not exist; the seam is IQuestionVersionCatalog, the plan is recorded in ADR 0002, and no speculative interface is added.
- FR-15 admin preview UI (GET detail provides the data, but there is no candidate-view rendering).
- FR-49 accommodations (entity, endpoints, extra-time application).
- Section 8 proctoring profiles, Exam.ProctoringProfileId and IProctorPolicy (M6). The FR-12 'proctoring profile' item is deferred.
- Section 9 staggered entry windows.
- The ExamSeries entity and a series picker (SeriesId stays an optional opaque id).
- Institute entity, teacher-to-batch assignment and ownership transfer, and batch reports.
- FR-39 invite notifications (NotificationChannel, email/SMS delivery of codes and links); BatchMember.MarkInviteSent; bulk invite generation per batch.
- The FR-16 candidate dashboard (upcoming/ongoing/completed) and GET /v1/exams?status=upcoming for candidates. Only a minimal enrollments list ships.
- Exam Runtime (M4) enforcement of enrollment, consent and schedule at attempt start (the IEnrollmentQuery seam is provided).
- Blocking enrollment for minors (the FR-43 gate is enforced at exam access in M4 and via PR3's consent flow). Turning roster guardian contacts into guardian accounts or links.
- Rescheduling or unpublishing published exams, question reordering, section reordering, and question groups (passages).
- Redis-backed distributed rate limiting, and production reverse-proxy or deployment configuration.
- FR-51 i18n and a broader NFR-7 accessibility audit (only the touched components get label and aria-live fixes).
- Guardian module endpoints and web guardian flows (PR3), and Identity/auth hardening (PR1).

## verification
- cd apps/api && dotnet build ExamPlatform.slnx -c Release (warnings-as-errors for nullable must stay clean)
- cd apps/api && dotnet test tests/SharedKernel/ExamPlatform.SharedKernel.UnitTests: run it locally on Windows to prove IANA 'Asia/Kolkata' resolves under InvariantGlobalization. CI runs it on ubuntu and on the new windows-latest job.
- cd apps/api && dotnet test tests/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.UnitTests tests/Modules/Batch/ExamPlatform.Modules.Batch.UnitTests tests/Modules/Invite/ExamPlatform.Modules.Invite.UnitTests tests/Modules/Identity/ExamPlatform.Modules.Identity.UnitTests
- cd apps/api && dotnet test tests/ExamPlatform.ArchitectureTests (new Contracts rows and the Application-only-via-Contracts facts pass, and the positive controls are non-empty)
- cd apps/api && dotnet test tests/ExamPlatform.IntegrationTests (Docker running). Focus runs: --filter FullyQualifiedName~InviteAcceptFlowTests, ~RosterImportFlowTests, ~ExamAuthoringFlowTests, ~ExamBatchAssignmentFlowTests, ~M3AuditFlowTests
- For each module (ExamAuthoring, Batch, Invite): dotnet ef migrations has-pending-model-changes --project src/Modules/<M>/ExamPlatform.Modules.<M>.Infrastructure --startup-project src/Host/ExamPlatform.Api --context <M>DbContext should report no changes
- Apply migrations to an existing dev database created from main + PR1-3 (docker compose up -d postgres; dotnet run --launch-profile http). Startup must migrate without error, which proves the SQL backfills: code hashing, email lowercasing and schedule nulling.
- cd apps/web && npm run lint && npm test -- --watch=false && npm run build
- Manual end-to-end with API http://localhost:5239 and npm start:
1. ExamAdmin creates an exam with a blank series, sets config and adds a section and question.
2. Schedules 10:00 Asia/Kolkata and checks the detail shows 04:30Z.
3. Creates a batch and imports a CSV: first with one bad row (422 report, nothing imported), then a clean file.
4. Assigns the exam to the batch, publishes, creates an invite for a member and generates a code (shown once).
5. As that candidate, opens /invites/accept?code=..., sees the enrollment on /home, and sees the member marked Registered in the roster.
6. A different candidate using the same code gets the 'not valid for this account' message.
7. Rapid repeated attempts eventually return 429.
8. A candidate token hides the staff nav, and direct navigation to /exams redirects to /home.
- Security spot checks: GET /v1/invites responses never contain 'code' or a hash; the invite.InviteCodes table holds only 64-hex CodeHash values; audit metadata for roster import and invite events contains no email or phone.

