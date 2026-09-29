# ADR 0001: Platform Stack and Module Boundary Strategy

- Status: Accepted
- Date: 2026-09-29

## Context

`exam-platform-requirements.md` section 0.3 requires an ADR proposing the technology stack before any code is written, and says its own section 11 ("Architecture") is a suggestion, not a decision. Section 11 suggests a modular monolith with 11 bounded-context modules (Identity & Access, Consent & Privacy, Question Bank, Exam Authoring, Batches & Invites, Exam Runtime, Proctoring, Evaluation & Ranking, Analytics & Reporting, Notifications, Admin & Audit) that "communicate through interfaces and domain events, never by reaching into each other's tables," with an explicit path to extracting Exam Runtime, Proctoring, and Analytics as separate services once concurrency crosses the tiers defined in section 9.

The repository is greenfield: no code exists yet. This ADR records the actual stack decision (superseding section 11's suggestions where the product owner made an explicit choice) and the concrete strategy for making the "modules never reach into each other's tables" rule enforceable rather than aspirational.

## Decision

**Backend: .NET 10, ASP.NET Core, Minimal APIs.** Chosen over the doc's suggested NestJS/FastAPI/Django by explicit product-owner direction.

**Frontend: Angular 22**, standalone components, PWA via the official `@angular/pwa` schematic. Chosen over the doc's suggested React/Next.js by explicit product-owner direction.

**Repository layout: monorepo** — `apps/api` (.NET solution), `apps/web` (Angular workspace), `docs/adr` (architecture decision records).

**Architecture: modular monolith, Clean Architecture per module**, composed by a thin Host:

```
apps/api/src/
  SharedKernel/
    ExamPlatform.SharedKernel.Domain          # Entity, AggregateRoot, ValueObject, IDomainEvent, DomainException
    ExamPlatform.SharedKernel.Application      # Clock, IDomainEventDispatcher, IModuleInstaller
    ExamPlatform.SharedKernel.Infrastructure   # SystemClock, InProcessDomainEventDispatcher, UTC datetime conventions
  Modules/
    Identity/    {Domain, Application, Infrastructure, Endpoints}
    Consent/     {Domain, Contracts, Application, Infrastructure, Endpoints}
    Admin/       {Domain, Contracts, Application, Infrastructure, Endpoints}
    # QuestionBank/, ExamAuthoring/, BatchesInvites/, ExamRuntime/, Proctoring/,
    # EvaluationRanking/, AnalyticsReporting/, Notifications/ join later as the
    # same project shape — no restructuring needed.
  Host/
    ExamPlatform.Api                           # composition root only
```

Reference rules, enforced by project references and checked by an `ExamPlatform.ArchitectureTests` suite (NetArchTest.Rules):

- `*.Domain` → `SharedKernel.Domain` only. No EF Core, no ASP.NET Core, no other module.
- `*.Application` → own `Domain`, `SharedKernel.Application`, and **other modules' `Contracts` projects only** — never another module's Domain/Application/Infrastructure.
- `*.Infrastructure` → own `Domain` + `Application`, `SharedKernel.Infrastructure`, EF Core/Npgsql.
- `*.Contracts` → `SharedKernel.Domain` only, kept dependency-free so any future module can reference it cheaply.
- `*.Endpoints` → own `Application` + `Infrastructure`, ASP.NET Core.
- `Host` → only each module's `Endpoints` project plus `SharedKernel.*`. The Host never references a module's internals directly, so it cannot become a bypass around the boundary.

A module registers itself with the Host via a self-contained `IModuleInstaller` (`AddModule`, `MapEndpoints`); the Host's module list is the only thing that changes when a new module is added — an Open/Closed-compliant composition mechanism.

**Data:** PostgreSQL as the primary store, one `DbContext` per module (`IdentityDbContext`, `ConsentDbContext`, `AdminDbContext`), each in its own Postgres schema within a single physical database for now. `ConsentDbContext` has no `DbSet<User>`, so Consent code cannot query Identity's table — the boundary is compiler-enforced, not a convention. Redis is provisioned (per section 11) for future sessions/timers/leaderboards use but is not consumed by any code in this slice.

**Messaging:** none yet. In-process domain events only, dispatched via a hand-rolled `IDomainEventDispatcher` from an EF Core `SaveChangesInterceptor`. RabbitMQ/Kafka are deferred until a module genuinely needs durable async work (grading, notifications — expected around Exam Runtime, M4+).

**Testing:** xUnit, NSubstitute, Testcontainers.PostgreSql, NetArchTest.Rules, coverlet.

**CI/CD:** GitHub Actions — a .NET build/test job and an Angular lint/build/test job, running in parallel.

**Local dev:** docker-compose with `postgres:16` and `redis:7`.

## Alternatives considered

- **NestJS / FastAPI / Django** (the doc's own section 11 suggestions) — superseded by explicit product-owner direction for .NET 10. .NET's built-in dependency injection container and interface-first idioms map directly onto the doc's DIP-heavy design (section 1.2), and EF Core's migration tooling and native minimal-API + OpenAPI support give a single, statically-typed runtime path toward a performance-sensitive Exam Runtime later.
- **React/Next.js** (the doc's own section 11 suggestion) — superseded by explicit product-owner direction for Angular 22. Angular's built-in DI and RxJS/signals-based reactivity mirror the same DIP/interface-heavy discipline used on the backend; standalone components keep the framework's opinionated, batteries-included structure without NgModule boilerplate; and `@angular/pwa` gives first-party, officially maintained PWA support (service worker, manifest, update flow) satisfying the doc's PWA requirement (section 2.4) directly, without evaluating third-party plugins. React/Next.js remains a reasonable alternative — larger ecosystem, and it was the doc's own original suggestion — but the decision here is the product owner's explicit override, recorded as-is rather than re-derived.
- **MediatR vs. a hand-rolled `IDomainEventDispatcher`** — MediatR's 2025 licensing change makes it a real cost/risk to adopt repo-wide for a project of this expected lifetime. A ~50-line dispatcher resolving `IEnumerable<IDomainEventHandler<TEvent>>` from DI is trivial to own, fully OCP-compliant (new handlers register via DI, no call-site changes), and can be swapped later without touching domain code.
- **One `DbContext` per module vs. a single shared context** — a shared context would make the "modules never reach into each other's tables" rule a convention people could violate. Per-module contexts make it a compile error. Accepted trade-off: no cross-schema foreign keys; cross-module references (e.g. `ConsentRecord.SubjectId`) are application-enforced only.
- **NSubstitute vs. Moq** — NSubstitute chosen to sidestep the 2023 Moq telemetry controversy; equally capable for this project's needs.
- **EF Core vs. Dapper** — EF Core chosen for productivity and migrations at launch (tier-1) scale. Dapper, or a dedicated store like ClickHouse/OpenSearch, remain candidates for the future Analytics & Reporting module's read-heavy queries (section 11).
- **Native `Microsoft.AspNetCore.OpenApi` + Scalar UI vs. Swashbuckle** — the native OpenAPI generator paired with `Scalar.AspNetCore` is the actively maintained, .NET-10-idiomatic combination; Swashbuckle remains the more familiar fallback if issues arise.

## Consequences

**Positive:** module boundaries are enforced by the compiler and by architecture tests, not just documentation; the per-module `DbContext`/schema split gives a clean path to the physical extraction described in section 9's scale tiers (a module can move to its own database by changing one connection string); strong static typing on both ends reduces the silent-failure risk the doc's section 1.2 calls out.

**Negative:** more project-file ceremony per module (4–5 projects instead of one); no database-level foreign key constraints across module schemas, so referential integrity between modules (e.g. a deleted `User` orphaning a `ConsentRecord`) is an application-level, not database-level, guarantee — a monitored and explicitly accepted risk for this slice, mitigated for now by Identity using soft-delete only.

## Follow-ups

- **India-region cloud infrastructure-as-code** is out of scope for this ADR and this slice — it's blocked on `exam-platform-requirements.md` section 17's open question 4 (preferred cloud provider), and this session has no cloud credentials to provision it regardless.
- **DPDP/legal artifacts** (data map, consent copy, legal opinion on proctoring for minors — section 7) are M0/M7 deliverables that need counsel and a compliance team, not an engineering slice.
- **StackExchange.Redis wiring**: deferred until a module has a genuine need for distributed state — most likely Exam Runtime's server-side timers (M4).
- **RabbitMQ/Kafka**: deferred until async, durable work (grading, notifications) exists.

## Process deviations (recorded once, not per-commit)

- **Branch naming:** `exam-platform-requirements.md` section 1.1 wants milestone branches named `SP/feature/<slug>` (uppercase). This work stays on the harness-assigned branch `sp/eager-franklin-94y6x7` instead, because the session's own instructions require developing on that pre-created branch and forbid creating or switching to a different one.
- **No co-author trailers:** per section 1.1 and explicit instruction, no commit in this repository carries a `Co-authored-by:` line or any AI-attribution trailer.
