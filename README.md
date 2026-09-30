# Online Exam Platform

An invite-only, India-first online exam platform (MCQ-based mock tests, chapter tests, and contests) for school and competitive-exam preparation.

See [`exam-platform-requirements.md`](./exam-platform-requirements.md) for the full product requirements and delivery milestones, and [`docs/adr/`](./docs/adr) for architecture decision records — start with [ADR 0001](./docs/adr/0001-stack.md) for the stack decision and module boundary strategy.

## Repository layout

```
apps/api/   .NET 10 backend — modular monolith, Clean Architecture per module
  src/SharedKernel/   Cross-cutting building blocks every module depends on
  src/Modules/        Identity, Consent, Admin (more join later — see ADR 0001)
  src/Host/           ExamPlatform.Api — the composition root
  tests/              Per-module unit tests, ArchitectureTests, IntegrationTests
apps/web/   Angular 22 frontend (PWA)
docs/adr/   Architecture decision records
```

## Status

Slice 1: stack ADR, monorepo scaffold, and the engineering-only pieces of Milestone M1 — RBAC + OTP/password login (FR-1–FR-4), a consent ledger (FR-44), and an audit log (FR-40). India-region infrastructure and DPDP/legal artifacts are out of scope for this slice (see ADR 0001's follow-ups).

Slice 2: an Angular frontend for the above — registration, OTP verification, password login (with the 2FA hand-off for `RequiresTwoFactor` roles), password reset, a profile page, and a consent grant/status/withdraw page. CI/CD and India-region infrastructure-as-code remain deferred, per ADR 0001's follow-ups; the milestone's invite lifecycle, guardian consent portal, and batches belong to M3, not this slice.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or install via your OS package manager — e.g. `apt-get install dotnet-sdk-10.0` on recent Ubuntu)
- [Node.js 22.22.3+](https://nodejs.org/) and npm (required by Angular 22 — check with `node --version`)
- [Docker](https://docs.docker.com/get-docker/) and Docker Compose, for local Postgres/Redis

## Local development

### 1. Start Postgres and Redis

```bash
cp .env.example .env   # review/edit if needed; defaults work for local dev
docker compose up -d postgres redis
```

### 2. Run the API

```bash
cd apps/api
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Host/ExamPlatform.Api --urls http://localhost:5080
```

On first run (Development only), the Host applies each module's EF Core migrations and seeds reference data (RBAC roles/permissions, consent notice versions) automatically — see `IModuleInstaller.MigrateAndSeedAsync` in each module's Endpoints project. No separate migration step is needed for local dev.

Once running:
- OpenAPI document: `http://localhost:5080/openapi/v1.json`
- Interactive API docs (Scalar): `http://localhost:5080/scalar/v1`
- Health check: `http://localhost:5080/v1/health`

OTP codes are logged to the console instead of sent by email/SMS (`LoggingOtpSender` — a deliberate dev-only stand-in until a real Notifications module exists; see the class's docstring). Watch the API's console output for `[DEV OTP SENDER] ...` lines when testing login/registration.

### 3. Run the web app

```bash
cd apps/web
npm install
npm start   # ng serve, http://localhost:4200
```

The app shell's health widget calls the API's `/v1/health` endpoint to confirm the two are wired together. From there: `/register` → `/verify-otp` → `/profile` walks through account creation, and `/login` supports both password and one-time-code sign-in. `/profile` and `/consent` require being signed in.

## Running tests

```bash
# Backend: unit tests, architecture (module boundary) tests, and integration
# tests. Integration tests spin up a disposable Postgres via Testcontainers —
# Docker must be running.
cd apps/api
dotnet test ExamPlatform.slnx

# Frontend
cd apps/web
npm test -- --watch=false
npm run lint
```

## Database migrations

Each module owns its own EF Core `DbContext` and migrations (see ADR 0001). To add a new migration for a module:

```bash
cd apps/api
dotnet tool install --global dotnet-ef   # once, if not already installed

dotnet ef migrations add <MigrationName> \
  --project src/Modules/<Module>/ExamPlatform.Modules.<Module>.Infrastructure \
  --startup-project src/Host/ExamPlatform.Api \
  --context <Module>DbContext \
  --output-dir Migrations
```

## Requirement traceability

Commit messages and PR descriptions reference the requirement IDs (`FR-#`, `NFR-#`) they implement, per `exam-platform-requirements.md` section 0.5. Compliance-sensitive areas (consent, RBAC, audit) have explicit tests alongside the feature.
