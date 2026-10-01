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
- [Authentication & Security](#authentication--security)
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

### ✅ Completed Features (Milestones M1 & M3)

#### **M1: Authentication & Authorization**
- User registration with email/phone verification via OTP
- Password login with mandatory two-factor authentication for staff and admin roles; candidates sign in with a one-time code
- Role-based access control (RBAC) with granular permissions
- Session management: JWT access tokens tied to a server-side session (one active session per user, revoked on logout)
- Consent ledger for tracking data usage agreements
- Audit logging for security compliance

#### **M3: Exam & Batch Management**
- **Exam Authoring**: Create exams with configurable sections, questions, and metadata
- **Exam Scheduling**: Set exam windows with timezone support and late-entry deadlines
- **Batch Management**: Create and manage candidate batches with member rosters
- **Invite System**: Generate invitation codes, track invitations, manage lifecycle
- **Guardian Portal**: Register guardians, link candidates, manage consent delegation

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

### Milestone M3: Exam Authoring & Enrollment (✅ Complete)
**Backend**: Exam authoring, batch management, invite system, guardian portal (complete CQRS, EF Core, REST endpoints)  
**Frontend**: Exam builder & scheduler, batch creation & roster, invite management, guardian portal UI  
**Status**: ✅ Merged to main, all features implemented

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

**Endpoints once running:**
- 🏥 Health Check: `http://localhost:5080/v1/health`
- 📖 API Docs (Scalar UI): `http://localhost:5080/scalar/v1` (Development only)
- 📄 OpenAPI Schema: `http://localhost:5080/openapi/v1.json` (Development only)

**📌 Dev Note:** OTP codes and password-reset links print to the API console instead of being sent via email/SMS, as `[DEV ONLY - never enabled outside Development]` messages with the destination masked (`j***@example.com`). That is the `Identity:OtpDelivery:Provider` setting `DevelopmentLog` from `appsettings.Development.json`, and it is accepted only in the Development environment: **any other environment fails to start** until a real sender exists (see [Authentication & Security](#authentication--security)). Watch the API logs when testing login/registration.

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
3. Login: `/login` → candidates sign in with a one-time code; staff and admin accounts use their password and then a one-time code
4. Explore: `/profile` (edit details), `/consent` (grant permissions), exam/batch features

The health widget confirms frontend↔backend connectivity.

---

## Authentication & Security

How sign-in, sessions and the API's abuse protections behave, and the settings that control them. Defaults live in `apps/api/src/Host/ExamPlatform.Api/appsettings.json`; any key can be overridden by an environment variable (`:` becomes `__`, e.g. `Identity__RateLimits__PasswordLogin__PermitLimit`).

### Signing in (FR-1, FR-3)

| Account | How it signs in |
|---------|-----------------|
| **Candidate** (no password) | Request a one-time code with `POST /v1/auth/otp/request`, then verify it with `POST /v1/auth/otp/verify`. Registration works the same way: the code sent after `POST /v1/auth/register` activates the account. |
| **Staff and admin** (any role that requires 2FA) | Password with `POST /v1/auth/login`, which answers `requiresTwoFactor` plus an `otpChallengeId`. The one-time code sent to the account's own email or phone, verified with `POST /v1/auth/otp/verify`, completes the sign-in. |

Staff cannot sign in with a one-time code alone. A staff address that asks for an OTP-only login gets the same answer as an unknown address, with nothing sent, and a code issued for a login or registration can never complete a staff sign-in (`403 two_factor_login_required`). The web login page says so.

- **No account enumeration.** For a well-formed request, `POST /v1/auth/otp/request` answers `200` with an `otpChallengeId` whether the address is unknown, locked, pending verification or staff, and `POST /v1/auth/password-reset/request` answers `200` for every email.
- **One-time codes** are 6 digits, valid for 10 minutes and usable once (a replay is `400 otp_already_used`). Five wrong guesses lock the challenge (`429 otp_attempts_exceeded`); every guess is saved, even a refused one, so the count cannot be dodged by retrying. Asking for a new code for the same destination and purpose supersedes the older ones (`400 otp_superseded`).
- **Locked accounts** (suspended or deactivated) cannot sign in: `403 account_locked`.
- **Registration input** is validated in the domain, so every caller gets the same rules: `dateOfBirth` is required, not later than UTC today plus one day and not more than 120 years ago (`400 invalid_date_of_birth`); `displayName` is 1 to 200 characters once trimmed, here and on `PUT /v1/me/profile` (`400 invalid_display_name`); an email is at most 320 characters and a phone number at most 20 (`400 invalid_contact`); `otpChannel` is `Email` or `Sms` and must match the contact supplied (`400 invalid_otp_channel`, `400 contact_channel_mismatch`). A request body the API cannot read (invalid JSON, a field of the wrong type) is `400 invalid_request`. The web forms mirror these limits in `apps/web/src/app/auth/validators.ts`; change both together.

> There is no staff provisioning endpoint yet, and the seeder creates roles and permissions but no users. A staff sign-in on a development database therefore needs a user with a password hash and a 2FA role created by hand.

### Passwords (FR-3)

- 12 to 128 characters, with no composition rules (NIST SP 800-63B), and the password must not contain the part of the account's email before the `@` (case-insensitive, checked when that part is at least 3 characters): `400 weak_password`.
- `POST /v1/auth/password-reset/request` sends a link that is valid for 30 minutes. Only the newest link works. An account with no password (an OTP-only candidate) gets no link, and the answer is the same as for an unknown email.
- `POST /v1/auth/password-reset/reset` takes `passwordResetTokenId`, `token` and `newPassword` (the link carries the first two as `<id>:<token>`). A link works once. A successful reset ends every session the account has, so its old access tokens get `401 session_revoked`.

### Sessions and logout (FR-4)

A user has one active session: signing in again supersedes the previous one. The access token (a JWT) carries the session id as its `sid` claim and lives exactly as long as the session (12 hours). **Every authenticated request checks that session**, so a token stops working the moment its session is superseded, logged out, expired or its account locked, not when the token's own lifetime runs out.

A refused session is a `401` whose ProblemDetails `title` is one of these stable codes (the web app reads the title to explain the sign-out on the login page):

| `title` | Meaning |
|---------|---------|
| `session_unknown` | The token has no session id, or no such session exists |
| `session_superseded` | The account signed in somewhere else |
| `session_revoked` | The session was ended: logged out, or a password reset |
| `session_expired` | The session (and so the token) is past its expiry |
| `account_locked` | The account is suspended or deactivated |

`POST /v1/auth/logout` (bearer token required) revokes the caller's session and answers `204`. Using the same token again, even to log out, is `401 session_revoked`. The web app's **Log out** calls it and clears the local session whether or not the call succeeds. A token that is missing, malformed or wrongly signed gets a plain `401` with no session code.

Every authenticated request costs one indexed lookup of its session; if that ever needs caching, the cache must be invalidated on revoke or FR-4 regresses.

### One-time code delivery (NFR-6)

`Identity:OtpDelivery:Provider` picks the adapter that delivers codes and password-reset links. The only provider today is `DevelopmentLog`, which writes the code to the API log instead of sending it, and it is **allowed only when `ASPNETCORE_ENVIRONMENT=Development`** (`appsettings.Development.json` sets it):

```
[DEV ONLY - never enabled outside Development] Email code for j***@example.com: 123456
```

The destination is masked (an email keeps its first character and domain, a phone number its last two digits); the code is printed because signing in during development depends on it.

`appsettings.json` deliberately leaves the provider unset, so **in any other environment the API refuses to start** (an options-validation error naming `Identity:OtpDelivery`) until a real email/SMS adapter exists (the Notifications module, FR-39). Whoever owns deployments needs to know this. An integration test that boots the host outside Development must replace the sender; the recipe is on `OtpDeliveryOptionsValidator`.

### Rate limiting (NFR-5)

Limits are per client IP, in fixed windows, kept in-process. Every request counts against the global limit, and the auth routes also count against their own:

| Setting | Applies to | Default |
|---------|------------|---------|
| `RateLimiting:Global` | every request | 60 per 60 s |
| `Identity:RateLimits:OtpRequest` | `POST /v1/auth/otp/request` and `POST /v1/auth/register` (they share one budget) | 20 per 5 min |
| `Identity:RateLimits:OtpVerify` | `POST /v1/auth/otp/verify` | 30 per 5 min |
| `Identity:RateLimits:PasswordLogin` | `POST /v1/auth/login` | 10 per 5 min |
| `Identity:RateLimits:PasswordReset` | `POST /v1/auth/password-reset/request` and `/reset` (they share one budget) | 5 per 15 min |

Each setting has `PermitLimit` and `WindowSeconds`, and both must be positive or the API refuses to start, naming the setting. A rejected request gets `429` with a ProblemDetails body whose `title` is `rate_limited`, plus a `Retry-After` header in seconds. Tune the limits per environment: a school whose classroom shares one public IP needs more than the defaults.

### Running behind a proxy or load balancer (NFR-5)

The per-IP limits only work if the API sees the real client address. It honours `X-Forwarded-For` and `X-Forwarded-Proto`, one hop, and **only from the proxies you list**:

```json
"ForwardedHeaders": {
  "KnownProxies": [ "10.0.0.5" ],
  "KnownNetworks": [ "10.1.0.0/16" ]
}
```

Both lists are empty by default, which trusts loopback only. Behind a real proxy they must be set, otherwise every client appears with the proxy's address and shares one rate-limit bucket. They must be lists (a JSON array, or indexed variables such as `ForwardedHeaders__KnownProxies__0`); a single value or an entry that is not an IP address or CIDR range stops the API from starting and names the key.

### Response headers and exposure (NFR-5)

- Every response carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` and `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, including error responses. (The Scalar UI is the one exemption from the CSP, and it exists only in Development.)
- Outside Development the API also sends `Strict-Transport-Security` (the ASP.NET Core default policy), error responses included. It is sent only on requests the API sees as HTTPS, which behind a TLS-terminating proxy relies on the `ForwardedHeaders` trust above. There is no HTTPS redirect: TLS ends at the proxy.
- The OpenAPI document and the Scalar UI are mapped only in Development.

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
POST   /v1/auth/register               (FR-1, FR-43)
POST   /v1/auth/login                  (FR-3)
POST   /v1/auth/otp/request            (FR-1)
POST   /v1/auth/otp/verify             (FR-1)
POST   /v1/auth/logout                 (FR-4)
POST   /v1/auth/password-reset/request (FR-3)
POST   /v1/auth/password-reset/reset   (FR-3)
POST   /v1/exams                       (FR-11)
POST   /v1/batches                     (FR-17)
POST   /v1/invites                     (FR-20)
POST   /v1/guardians                   (FR-22)
... (complete list in the Scalar UI at /scalar/v1, Development only)
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

**Last Updated:** 2026-10-01 | **Status**: ✅ M1 & M3 Complete, Deployed to Main
