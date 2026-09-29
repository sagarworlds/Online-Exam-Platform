# Online Exam Platform

An invite-only, India-first online exam platform (MCQ-based mock tests, chapter tests, and contests) for school and competitive-exam preparation.

See [`exam-platform-requirements.md`](./exam-platform-requirements.md) for the full product requirements and delivery milestones, and [`docs/adr/`](./docs/adr) for architecture decision records.

## Repository layout

```
apps/api/   .NET 10 backend (modular monolith, Clean Architecture per module)
apps/web/   Angular 22 frontend (PWA)
docs/adr/   Architecture decision records
```

## Status

Slice 1 in progress: stack ADR, monorepo scaffold, and the engineering-only pieces of Milestone M1 (RBAC + auth/OTP, consent ledger, audit log). See ADR [0001](./docs/adr/0001-stack.md) for the stack decision.

Local development instructions will be added as the scaffold lands.
