# Online Exam Platform

<div align="center">

![Badge: .NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Badge: Angular 22](https://img.shields.io/badge/Angular-22-dd0031?logo=angular&logoColor=white)
![Badge: TypeScript](https://img.shields.io/badge/TypeScript-5.6-3178c6?logo=typescript&logoColor=white)
![Badge: PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-336791?logo=postgresql&logoColor=white)

An **invite-only, India-first online exam platform** for MCQ-based mock tests, chapter tests, and competitive exam preparation. Built with a modular .NET backend and modern Angular frontend, featuring RBAC authentication, consent management, and educational batch administration.

</div>

---

## 📋 Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Tech Stack](#tech-stack)
- [Repository Structure](#repository-structure)
- [Status & Milestones](#status--milestones)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Development Guide](#development-guide)
- [Testing](#testing)
- [Architecture](#architecture)
- [Contributing](#contributing)

---

## Overview

The Online Exam Platform is a purpose-built solution for educational institutions and competitive exam preparation centers in India. It provides:

- **Secure Authentication**: Role-based access control (RBAC) with OTP and password-based login
- **Consent Management**: Ledger-based consent tracking for regulatory compliance
- **Exam Authoring**: Comprehensive tools for creating and scheduling exams
- **Batch Management**: Organize candidates into batches with roster management
- **Invite Workflow**: Generate and manage invitations for exam participation
- **Guardian Portal**: Parental/guardian consent delegation for underage candidates
- **Audit Logging**: Complete audit trail for compliance and security

---

## Features

### Features (Milestones M1 & M3)

#### **M1: Authentication & Authorization**
- User registration with email/phone verification via OTP
- Password-based login with optional two-factor authentication
- Role-based access control (RBAC) with granular permissions
- Session management with JWT tokens
- Consent ledger for tracking data usage agreements
- Audit logging for security compliance

#### **M3: Exam loop (see "The exam loop" below for the gaps)**
- **Question Bank**: single-answer multiple-choice questions with an answer key kept server-side
- **Exam Authoring**: sections, questions, scheduling (window, time per attempt, latest start), publish
- **Invites**: e-mailed (or hand-delivered) links; accepting one, from the invited address, enrolls the candidate
- **Exam Taking**: start, answer, submit and score with a server-held deadline
- **Admin Controls**: permission-aware admin navigation and per-route permission checks on the API
- **Batches and Guardians**: backend and screens exist, but are not connected to the loop yet

---

## Tech Stack

### Backend
- **.NET 10** - Modern cloud-native framework
- **Entity Framework Core 10** - ORM for data access
- **PostgreSQL 16** - Primary data store
- **Redis** - Session/cache store
- **Clean Architecture** - Modular, domain-driven design
- **CQRS Pattern** - Command/Query responsibility segregation

### Frontend
- **Angular 22** - Standalone components with reactive forms
- **TypeScript 5.6** - Type-safe development
- **RxJS** - Reactive programming
- **Tailwind CSS** - Utility-first styling (ready for adoption)

### Infrastructure & DevOps
- **Docker & Docker Compose** - Containerization and local development
- **GitHub Actions** - CI/CD pipeline
- **Testcontainers** - Integration test isolation

---

## Repository Structure

```
Online-Exam-Platform/
├── apps/
│   ├── api/                                    # .NET 10 Backend
│   │   ├── src/
│   │   │   ├── SharedKernel/                   # Shared abstractions, patterns
│   │   │   ├── Modules/
│   │   │   │   ├── Identity/                   # Authentication & Authorization (FR-1–FR-4)
│   │   │   │   ├── Consent/                    # Consent Ledger (FR-44)
│   │   │   │   ├── Admin/                      # Audit Logging (FR-40)
│   │   │   │   ├── ExamAuthoring/              # Exam Management (FR-11–FR-13)
│   │   │   │   ├── Batch/                      # Batch Management (FR-17–FR-19)
│   │   │   │   ├── Invite/                     # Invite Management (FR-20–FR-21)
│   │   │   │   └── Guardian/                   # Guardian Portal (FR-22–FR-24)
│   │   │   └── Host/
│   │   │       └── ExamPlatform.Api/           # Composition root, dependency injection
│   │   └── tests/
│   │       ├── ArchitectureTests/              # Module boundary validation
│   │       ├── IntegrationTests/               # API contract testing
│   │       └── [Module].UnitTests/             # Domain logic testing
│   │
│   └── web/                                    # Angular 22 Frontend (PWA)
│       ├── src/app/
│       │   ├── auth/                           # Authentication UI & services (M1)
│       │   ├── exam-authoring/                 # Exam builder & scheduler (M3)
│       │   ├── batch-management/               # Batch & roster management (M3)
│       │   ├── invite-management/              # Invitation workflows (M3)
│       │   ├── guardian-portal/                # Guardian registration & portal (M3)
│       │   └── shared/                         # Shared services, interceptors, guards
│       └── src/environments/                   # Environment configurations
│
├── docs/
│   └── adr/                                    # Architecture Decision Records
│       ├── 0001-stack.md                       # Tech stack & module strategy
│       └── ...
│
├── exam-platform-requirements.md               # Product requirements & acceptance criteria
├── docker-compose.yml                          # Local dev environment
├── .env.example                                # Environment variables template
└── README.md                                   # This file
```

---

## Status & Milestones

### Milestone M1: Identity & Consent (✅ Complete)
**Backend**: RBAC authentication, OTP/password login, consent ledger, audit logging  
**Frontend**: Registration, OTP verification, login, password reset, profile management, consent page  
**Status**: ✅ Merged to main, CI/CD pipeline green

### Milestone M3: Exam Authoring & Enrollment (first end-to-end loop works)
**Backend**: question bank, exam authoring (sections, questions, schedule, publish), invites that enroll a candidate, and exam taking with scoring (ExamRuntime module); batches and guardian links exist but are not part of the loop yet  
**Frontend**: admin question bank, books and chapters, exam builder/editor/scheduler (an exam can be limited to a book or chapters), invite creation and list, candidate invitation page, "My exams", exam-taking page with countdown and result  
**Status**: the loop below runs end to end against a live API and a real browser. Gaps are listed under it.

#### The exam loop

1. An administrator signs in (password + one-time code) and lands on `/admin`, which lists only the areas their permissions open.
2. **Questions** (`/admin/questions`): single-answer multiple choice, 2 to 6 options, exactly one correct. The question text is written in a rich-text editor (see [Question formatting](#question-formatting)); options are plain text.
3. **Books** (`/admin/books`, optional): a book has chapters; questions can be filed under a chapter and exams can be limited to a book or some of its chapters (see [Books, chapters and exam scope](#books-chapters-and-exam-scope)).
4. **Exams** (`/exams`): create an exam, add sections and questions, set the schedule (window, minutes per attempt, optional latest start), publish.
5. **Invites** (`/invites/create`): pick a published exam and a candidate's e-mail address. The invitation is e-mailed when `Smtp:Host` is configured; otherwise the page shows the link to pass on by hand.
6. The candidate opens the link (signing in or registering first), and accepts it. Only the account holding the invited e-mail address can accept; accepting enrolls them.
7. **My exams** (`/my-exams`): Start (the server fixes the deadline), answer (saved as they go), submit, and read the score. If time runs out the attempt is closed with the saved answers the next time anyone looks at it.

Configuration: `Smtp:Host`, `Smtp:Port`, `Smtp:EnableSsl`, `Smtp:User`, `Smtp:Password`, `Smtp:From` for e-mail, and `Invite:LinkBaseUrl` (default `http://localhost:4200`) for the address in invitation links. A sign-in that has to be done by hand in development reads its code from the API log (`Identity:OtpDelivery:Provider` = `DevelopmentLog`).

#### Question formatting

The question text is HTML from a rich-text editor: bold, italic, underline, subscript, superscript, bulleted and numbered lists, and pictures. The editor is only a convenience. **The API sanitizes every question's text before storing it** (it is shown to every candidate, so stored markup must never be able to run script), and the browser sanitizes it again where it is shown.

| What | Rule |
|------|------|
| Allowed markup | `p br strong em u s sub sup ul ol li blockquote pre code img`. Every attribute, style, class, link, form, frame and script is removed. |
| Readable text | At most 4000 characters, not counting markup. A question needs some text or a picture. |
| Pictures | At most 5 per question. Each must be a PNG, JPEG, GIF or WebP embedded in the question, at most 512 KB decoded. The editor shrinks a picture to 800 px and 300 KB before embedding it (a GIF is kept as it is or refused). Links to other sites and SVG are refused with a message, never silently dropped. |
| Stored size | At most about 1.5 million characters of HTML per question. |

Questions written before this change were plain text; the `RichQuestionText` migration converts them to escaped HTML so they look the same. Because pictures live inside the question, every response that carries the question carries them too (the admin question list returns up to 200 questions); if that becomes heavy, the upgrade path is an image upload endpoint with cacheable URLs.

#### Books, chapters and exam scope

A **book** (name, optional subject such as "Maths", optional description) holds ordered **chapters**. A question may be filed under one chapter of one book, or under none (existing questions stay unfiled). An **exam's scope** says where its questions may come from: **anywhere** in the bank (the default, and what every exam did before), **one whole book**, or **chosen chapters** of one book. Questions are still picked by hand; the scope limits what can be picked.

Books and chapters are for authors and administrators only (the `question.manage` permission, the same one the question bank uses). Candidates never see them.

| What | Rule |
|------|------|
| Where | `/admin/books` (list, create) and `/admin/books/:id` (rename, add and rename chapters, archive and restore). The question form has Book then Chapter selects (the choice is kept after saving, to enter many questions into one chapter), and the question list filters by book, chapter or "no chapter". |
| Limits | Book name 200 characters, subject 100, description 1000, chapter title 200. A chapter title is unique within its book. Chapters keep the order they were created in. |
| Archive, not delete | A book or chapter is archived, never deleted: it disappears from the pickers, keeps every question filed under it, and can be restored. An archived book takes no new chapters and an archived chapter or book takes no new questions. |
| Exam scope | Set when creating the exam or later on a draft (`PUT /v1/exams/{id}/scope`). Once an exam is published its scope is fixed. |
| Enforced by the API | Adding a question outside the exam's scope is refused with `409 question_outside_scope`, however the request is made; the editor's picker only offers in-scope questions. Narrowing a scope is refused, naming how many questions would be left outside it, until they are removed. |
| Authors without bank access | A user who may author exams but not read the question bank can still create exams, just not book-limited ones. |

Existing data is untouched: both migrations (`BooksAndChapters`, `ExamScope`) only add tables and nullable or defaulted columns, and existing exams read as "anywhere" (the `ExamScope` default was checked against a database that already held an exam).

**Known gaps**: no background worker, so abandoned attempts close lazily; one attempt per candidate per exam (no retakes); single-answer multiple choice only; candidates do not see books or chapters, and an exam cannot yet draw questions automatically by rule ("10 from chapter 2"); chapters cannot be reordered, and a question sits in one chapter only; no difficulty or topic tags; "Exam Series" is still only an optional ID with no entity behind it (a book is not a series); the question list shows the newest 200 questions per filter, with no paging; no tables, links, math or alt-text prompt in question text, and option text is plain; marking uses the exam's marking scheme (default +1 / 0 / 0) and the score is shown immediately whatever the result-release setting; batches do not yet feed enrollment; the guardian verification path is still a stub; e-mail needs a real SMTP server to be tried.

### Deferred (Per ADR 0001)
- **CD Pipeline**: Infrastructure-as-code, deployment automation
- **India Region**: DPDP compliance artifacts, localization
- **Advanced Features**: Analytics, proctoring, adaptive testing

See [`exam-platform-requirements.md`](./exam-platform-requirements.md) for full requirements and [`docs/adr/0001-stack.md`](./docs/adr/0001-stack.md) for architecture decisions.

---

## Prerequisites

| Component | Version | Installation |
|-----------|---------|--------------|
| **.NET SDK** | 10.0+ | [Download](https://dotnet.microsoft.com/download) or `apt-get install dotnet-sdk-10.0` |
| **Node.js & npm** | 22.22.3+ | [Download](https://nodejs.org/) (required for Angular 22) |
| **Docker & Compose** | Latest | [Get Docker](https://docs.docker.com/get-docker/) |
| **PostgreSQL** | 16 | Included in `docker-compose.yml` |
| **Redis** | Latest | Included in `docker-compose.yml` |

Verify installations:
```bash
dotnet --version       # Should be 10.0+
node --version         # Should be 22.22.3+
npm --version          # Should be 10.0+
docker --version       # Any recent version
docker compose version # Any recent version
```

---

## Getting Started

### Quick Start (5 minutes)

#### 1️⃣ Clone and Setup Environment

```bash
git clone https://github.com/sagarworlds/Online-Exam-Platform.git
cd Online-Exam-Platform
cp .env.example .env   # Review and adjust if needed (defaults work for local dev)
```

#### 2️⃣ Start Infrastructure (Postgres & Redis)

```bash
docker compose up -d postgres redis
# Verify: docker compose ps
```

#### 3️⃣ Run the Backend API

```bash
cd apps/api
ASPNETCORE_ENVIRONMENT=Development dotnet run \
  --project src/Host/ExamPlatform.Api \
  --urls http://localhost:5080
```

**What happens on first run:**
- EF Core migrations auto-apply for all modules
- Reference data seeds (RBAC roles/permissions, consent versions)
- No manual setup required ✅

This happens because `Database:MigrateAndSeedOnStartup` is `true` in the Development environment. It is `false` everywhere else; see [Migrate and Seed a Deployed Environment](#migrate-and-seed-a-deployed-environment).

**First administrator (optional):** staff sign in with a password plus a second-factor code, and nothing in the app creates a staff account, so a fresh development database has no administrator. To get one, store its credentials in [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) before the first run (the password stays out of the repository):

```bash
cd apps/api
dotnet user-secrets set "Identity:Bootstrap:AdminEmail" "admin@example.test" --project src/Host/ExamPlatform.Api
dotnet user-secrets set "Identity:Bootstrap:AdminPassword" "<a passphrase of at least 12 characters>" --project src/Host/ExamPlatform.Api
```

On startup an active `SuperAdmin` with that email is created if no account has it; an existing account is never changed. Sign in at `/login` with the email and password, then enter the code printed in the API log. The setting is honoured in the Development environment only and is ignored (with a warning) anywhere else.

**Endpoints once running:**
- 🏥 Health Check: `http://localhost:5080/v1/health`
- 📖 API Docs (Scalar UI): `http://localhost:5080/scalar/v1`
- 📄 OpenAPI Schema: `http://localhost:5080/openapi/v1.json`

**📌 Dev Note:** OTP codes print to console (`[DEV OTP SENDER]` messages) instead of being sent via email/SMS. Watch the API logs when testing login/registration.

#### 4️⃣ Run the Frontend (Angular)

In a new terminal:
```bash
cd apps/web
npm install
npm start   # Starts dev server at http://localhost:4200
```

**First login flow:**
1. Navigate to `http://localhost:4200`
2. Register: `/register` → enter OTP from API console → complete profile
3. Login: `/login` → password or OTP-based sign-in
4. Explore: `/profile` (edit details), `/consent` (grant permissions), exam/batch features

The health widget confirms frontend↔backend connectivity.

---

## Development Guide

### Understanding the Module Structure

Each module follows **Clean Architecture** with layered responsibilities:

```
ExamAuthoring (example)
├── Domain/             # Business logic, entities, value objects (Framework-agnostic)
├── Application/        # CQRS commands/queries, DTOs, handlers (Business logic flow)
├── Infrastructure/     # EF Core DbContext, repositories, external service adapters
└── Endpoints/          # REST API routes, dependency injection registration
```

This mirrors the `/Modules/Identity`, `/Modules/Consent`, etc. structure.

### Building the Backend

```bash
cd apps/api

# Restore dependencies and build
dotnet build ExamPlatform.slnx

# Format code
dotnet format ExamPlatform.slnx

# Run linting/analyzer
dotnet build ExamPlatform.slnx --no-restore /p:EnforceCodeStyleInBuild=true
```

### Building the Frontend

```bash
cd apps/web

# Install dependencies
npm install

# Development build (with hot reload)
npm start

# Production build
npm run build

# Linting
npm run lint

# Format code
npm run format
```

### Database Management

#### Add a Migration

Each module owns its own EF Core `DbContext`:

```bash
cd apps/api
dotnet tool install --global dotnet-ef   # One-time setup

# Add migration for a specific module
dotnet ef migrations add AddNewTable \
  --project src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Infrastructure \
  --startup-project src/Host/ExamPlatform.Api \
  --context ExamAuthoringDbContext \
  --output-dir Migrations
```

#### Migrate and Seed a Deployed Environment

Roles, permissions and consent notice versions are reference data written by each module's idempotent seeder, not by EF migrations. Applying only the migrations (for example with an EF migration bundle) therefore leaves a database where registration fails because the `Candidate` role does not exist. Outside Development nothing migrates or seeds on its own, so a deployment runs the Host once with `--migrate-and-seed` before the web replicas start:

```bash
ASPNETCORE_ENVIRONMENT=Production ConnectionStrings__Postgres="Host=...;Database=...;Username=...;Password=..." dotnet ExamPlatform.Api.dll --migrate-and-seed
```

It applies every module's pending migrations and seeders, logs `Migrated and seeded <Module>` for each, and exits with code 0 without starting the web host, so the connection string is the only setting it needs. Run it as a pre-deploy job or an init container, and as **one** process at a time: two concurrent runs can race on the unique indexes of `Roles.Name` and `Permissions.Code`. Seeding only ever adds missing data, so running it on every deploy is safe. Permissions are copied into the access token at sign-in, so users get a newly added permission the next time they sign in.

`Database:MigrateAndSeedOnStartup` does the same at the start of a normal run. Leave it `false` (the default outside Development) when several replicas start together. The reasoning is in [ADR 0002](./docs/adr/0002-migrations-and-reference-data-seeding.md).

#### View Migrations

```bash
dotnet ef migrations list \
  --project src/Modules/[Module]/ExamPlatform.Modules.[Module].Infrastructure \
  --startup-project src/Host/ExamPlatform.Api \
  --context [Module]DbContext
```

---

## Testing

### Backend Tests

```bash
cd apps/api

# Run all tests (unit, integration, architecture)
# ⚠️ Integration tests require Docker to be running
dotnet test ExamPlatform.slnx

# Run specific test category
dotnet test ExamPlatform.ArchitectureTests
dotnet test ExamPlatform.Modules.Identity.IntegrationTests
dotnet test ExamPlatform.Modules.ExamAuthoring.UnitTests

# Run with coverage
dotnet test ExamPlatform.slnx /p:CollectCoverage=true
```

### Frontend Tests

```bash
cd apps/web

# Run tests (watch mode)
npm test

# Run tests once (CI mode)
npm test -- --watch=false

# Run linting
npm run lint

# Generate coverage
npm test -- --coverage --watch=false
```

### End-to-End Testing

Playwright tests verify full user workflows (registration → exam creation → invitations):

```bash
cd apps/web

# Requires both API and frontend running
npm run e2e
```

---

## Architecture

### Design Principles

- **Clean Architecture**: Layers (Domain → Application → Infrastructure → Endpoints) with no cross-cutting dependencies
- **CQRS**: Command/Query responsibility segregation via Mediator pattern (MediatR)
- **DDD**: Domain-driven design with aggregates, value objects, and domain events
- **SOLID**: Single responsibility, Open/Closed, Liskov substitution, Interface segregation, Dependency inversion
- **Per-Module Database**: Each module owns its schema and DbContext (no shared databases)

### Module Boundaries (See ADR 0001)

| Module | Responsibility | Models |
|--------|-----------------|--------|
| **Identity** | Authentication, authorization, RBAC | User, Role, Permission, Session |
| **Consent** | Consent tracking, regulatory compliance | Consent, ConsentPurpose, NoticeVersion |
| **Admin** | Audit logging, compliance | AuditLog, AuditEntry |
| **ExamAuthoring** | Exam creation, scheduling, metadata | Exam, ExamSection, ExamQuestion, ExamSchedule |
| **Batch** | Candidate grouping, roster management | Batch, BatchMember, BatchRoster |
| **Invite** | Invitation lifecycle, code generation | Invite, InviteCode, InviteStatus |
| **Guardian** | Guardian registration, candidate linking | Guardian, GuardianLink, LinkVerification |

### HTTP API Versioning

All endpoints use `/v1/` prefix:
```
POST   /v1/auth/register               (FR-1)
POST   /v1/auth/login                  (FR-2)
POST   /v1/auth/otp/request            (FR-3)
POST   /v1/auth/otp/verify             (FR-4)
POST   /v1/exams                       (FR-11)
POST   /v1/batches                     (FR-17)
POST   /v1/invites                     (FR-20)
POST   /v1/guardians                   (FR-22)
... (complete list in Scalar UI at /scalar/v1)
```

---

## Contributing

### Commit Message Convention

Reference requirement IDs in commit messages:

```
[Feature Description] (FR-11, FR-12)

- Brief explanation of why this change was made
- Impact on system behavior
```

Example:
```
Add exam authoring endpoints (FR-11, FR-12, FR-13)

- Implements exam creation with sections and questions
- Adds scheduling with timezone and late-entry support
- Includes CQRS handlers and EF Core mapping
```

### Code Style

**Backend (.NET)**
- Follow [C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Run `dotnet format` before committing
- Use PascalCase for public members, camelCase for private

**Frontend (TypeScript/Angular)**
- Follow [Angular Style Guide](https://angular.dev/guide/styleguide)
- Run `npm run lint -- --fix` before committing
- Standalone components, reactive forms, inject() API

### Testing Requirements

- Unit tests for domain logic and application services (target: >80% coverage)
- Integration tests for API contracts
- Architecture tests to enforce module boundaries
- Frontend component smoke tests

### Pull Request Checklist

- [ ] All tests pass (`dotnet test` / `npm test`)
- [ ] No linting errors (`dotnet format` / `npm run lint`)
- [ ] Builds successfully (`dotnet build` / `npm run build`)
- [ ] Commit messages reference requirements (`FR-#`, `NFR-#`)
- [ ] Architecture decisions documented (if applicable)
- [ ] README updated (if user-facing changes)

---

## Requirement Traceability

Functional Requirements (FR) and Non-Functional Requirements (NFR) are tracked in [`exam-platform-requirements.md`](./exam-platform-requirements.md). Each commit and PR references the requirements it implements:

```
Example: "Add exam authoring endpoints (FR-11, FR-12, FR-13)"
```

Compliance-sensitive features (consent, RBAC, audit) have explicit test coverage and ADR documentation.

---

## Support & Resources

- 📖 **Product Requirements**: See [`exam-platform-requirements.md`](./exam-platform-requirements.md)
- 🏗️ **Architecture Decisions**: See [`docs/adr/`](./docs/adr/) (start with [ADR 0001](./docs/adr/0001-stack.md))
- 🐛 **Issues & Bugs**: [GitHub Issues](https://github.com/sagarworlds/Online-Exam-Platform/issues)
- 💬 **Discussions**: [GitHub Discussions](https://github.com/sagarworlds/Online-Exam-Platform/discussions)

---

## License

MIT
---

**Last Updated:** 2026-10-02 | **Status**: M1 complete; the M3 exam loop (question → exam → invite → take → score) works end to end, with the gaps listed above
