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

### ✅ Completed Features (Milestones M1 & M3)

#### **M1: Authentication & Authorization**
- User registration with email/phone verification via OTP
- Password-based login with optional two-factor authentication
- Role-based access control (RBAC) with granular permissions
- Session management with JWT tokens
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

**Last Updated:** 2026-09-30 | **Status**: ✅ M1 & M3 Complete, Deployed to Main
