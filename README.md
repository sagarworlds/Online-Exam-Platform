# Online Exam Platform

<div align="center">

![Badge: .NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Badge: Angular 22](https://img.shields.io/badge/Angular-22-dd0031?logo=angular&logoColor=white)
![Badge: TypeScript](https://img.shields.io/badge/TypeScript-5.6-3178c6?logo=typescript&logoColor=white)
![Badge: PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-336791?logo=postgresql&logoColor=white)

An **invite-only, online exam platform** for MCQ-based mock tests, chapter tests, and competitive exam preparation. Built with a modular .NET backend and modern Angular frontend, featuring RBAC authentication, consent management, and educational batch administration.

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

### Features (Milestones M1 & M3)

#### **M1: Authentication & Authorization**
- User registration with email/phone verification via OTP
- Password login with mandatory two-factor authentication for staff and admin roles; candidates sign in with a one-time code
- Role-based access control (RBAC) with granular permissions
- Session management: JWT access tokens tied to a server-side session (one active session per user, revoked on logout)
- Consent ledger for tracking data usage agreements
- Audit logging for security compliance

#### **M3: Exam loop (see "The exam loop" below for the gaps)**
- **Question Bank**: single-answer multiple-choice questions with an answer key kept server-side; questions can be edited (wording only once candidates have answered), deleted when no exam holds them, and filed under a book and chapter one at a time or in bulk
- **Exam Authoring**: sections, questions, scheduling (window, time per attempt, latest start), publish
- **Invites**: e-mailed (or hand-delivered) links; accepting one, from the invited address, enrolls the candidate
- **Exam Taking**: start, answer, clear a response, mark questions for review, submit and score with a server-held deadline
- **Answer review**: after submitting, a candidate sees which answers were right and wrong, with the correct options, once the exam's author allows it
- **Extra attempts**: everyone has one attempt; an administrator can give a candidate another, and every later attempt shows questions and options shuffled
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
**Frontend**: admin question bank (create, edit, delete, file under a chapter, label with a difficulty and topics, filter by them, search by text), books and chapters, exam builder/editor/scheduler (an exam can be limited to a book or chapters, and a draft can be put right: questions and sections taken out, a section renamed, the name corrected, the draft deleted), invite creation and list, candidate invitation page, "My exams", exam-taking page with countdown and result  
**Status**: the loop below runs end to end against a live API and a real browser. Gaps are listed under it.

#### The exam loop

1. An administrator signs in (password + one-time code) and lands on `/admin`, which lists only the areas their permissions open.
2. **Questions** (`/admin/questions`): single-answer multiple choice, 2 to 6 options, exactly one correct. The question text is written in a rich-text editor (see [Question formatting](#question-formatting)); options are plain text. A question can be edited, deleted or filed under a book and chapter later (see [Editing, deleting and filing questions](#editing-deleting-and-filing-questions)).
3. **Books** (`/admin/books`, optional): a book has chapters; questions can be filed under a chapter and exams can be limited to a book or some of its chapters (see [Books, chapters and exam scope](#books-chapters-and-exam-scope)).
4. **Exams** (`/exams`): create an exam, add sections and questions, set the schedule (window, minutes per attempt, optional latest start), choose when candidates may see which answers were right (see [Answer review](#answer-review)), publish. A draft can be put right on the way (see [Putting a draft exam right](#putting-a-draft-exam-right)).
5. **Invites** (`/invites/create`): pick a published exam and a candidate's e-mail address. The invitation is e-mailed when `Smtp:Host` is configured; otherwise the page shows the link to pass on by hand.
6. The candidate opens the link (signing in or registering first), and accepts it. Only the account holding the invited e-mail address can accept; accepting enrolls them.
7. **My exams** (`/my-exams`): Start (this opens the instructions and system check, see [Instructions and system check](#instructions-and-system-check); the server fixes the deadline once the candidate has acknowledged them), answer (saved as they go; an answer can be cleared and a question marked for review, see [Sitting an exam](#sitting-an-exam-clearing-a-response-and-marking-for-review)), submit, and read the score, then review which answers were right once they are released. If time runs out the attempt is closed with the saved answers the next time anyone looks at it. A candidate asks for another attempt by contacting an administrator, who gives one on the exam's **Candidates and attempts** page (see [Extra attempts](#extra-attempts)).

Configuration: `Smtp:Host`, `Smtp:Port`, `Smtp:EnableSsl`, `Smtp:User`, `Smtp:Password`, `Smtp:From` for e-mail (one mail server and one sender for everything the platform sends: invitations and the answers to attempt requests; each module writes its own message and hands it to a shared `IMailSender`, which never logs a message's body or subject), and `Invite:LinkBaseUrl` (default `http://localhost:4200`) for the address in invitation links. A sign-in that has to be done by hand in development reads its code from the API log (`Identity:OtpDelivery:Provider` = `DevelopmentLog`). A candidate has no password and signs in on the **One-time code** tab; staff use the **Password** tab and then get a code. When a sign-in gets no code, the same log says why, so there is no waiting at a verify screen: for example `No code was sent for c***@example.com: this account has no password. Candidates sign in with the One-time code tab.` (no account for that address, a suspended account, a staff address on the code tab, or a wrong password). The caller is told nothing different, and any other delivery says nothing, since explaining would reveal which addresses have accounts.

#### Question formatting

The question text is HTML from a rich-text editor: bold, italic, underline, subscript, superscript, bulleted and numbered lists, and pictures. The editor is only a convenience. **The API sanitizes every question's text before storing it** (it is shown to every candidate, so stored markup must never be able to run script), and the browser sanitizes it again where it is shown.

| What | Rule |
|------|------|
| Allowed markup | `p br strong em u s sub sup ul ol li blockquote pre code img`. Every attribute, style, class, link, form, frame and script is removed. |
| Readable text | At most 4000 characters, not counting markup. A question needs some text or a picture. |
| Pictures | At most 5 per question. Each must be a PNG, JPEG, GIF or WebP embedded in the question, at most 512 KB decoded. The editor shrinks a picture to 800 px and 300 KB before embedding it (a GIF is kept as it is or refused). Links to other sites and SVG are refused with a message, never silently dropped. |
| Stored size | At most about 1.5 million characters of HTML per question. |

Questions written before this change were plain text; the `RichQuestionText` migration converts them to escaped HTML so they look the same. Because pictures live inside the question, every response that carries the question carries them too (the admin question list returns up to 200 questions); if that becomes heavy, the upgrade path is an image upload endpoint with cacheable URLs.

#### Multiple-answer questions

A question normally has exactly one correct option. An author can tick **More than one answer can be correct** in the question editor, then tick every correct option (at least one, and at least one left unticked). The candidate sees checkboxes and "Choose all the answers that apply", and each tick or untick saves the whole set; unticking the last one clears the answer.

| What | Rule |
|------|------|
| Marking | **All or nothing.** The question is right only when the options chosen are exactly the correct ones, in any order, with none missing and none extra, and earns the exam's "correct" marks. Any other non-empty choice is wrong and earns the "incorrect" marks; choosing nothing is unanswered. There is no partial credit, so a question is worth the same as any other and the exam's maximum score is unchanged. |
| Locked once answered | Like the answer key, whether a question takes one answer or several cannot change once a candidate has answered it, because stored scores were worked out against it. Only its wording can. |
| Saving an answer | `PUT /v1/me/attempts/{id}/answers/{questionId}` takes `{ "optionIds": [...] }` for a multiple-answer question. The old `{ "optionId": "..." }` still works for a single-answer one, and several options for a single-answer question are refused with 400 `invalid_answer`. Attempt responses carry `allowsMultiple`, `selectedOptionIds` and, for older clients, `selectedOptionId` (the first chosen). |
| Review | Each option shows whether it was chosen and whether it was correct, and the question says that exactly the correct set had to be chosen. |
| Storage | `Questions.AllowsMultiple` (default false) and `AttemptAnswers.SelectedOptionIds` (a `uuid[]`, replacing the single `SelectedOptionId`; the migration carries every existing answer over as a set of one). |

#### Answer review

After submitting, a candidate sees their score at once, and then, once the exam's author allows it, a page showing every question with each option marked as the correct one and as the one they chose, whether the answer was correct, wrong or missing, the marks it earned (negative marking included) and the totals.

The author chooses when, per exam, on the exam page (**Answer review**; this may also be changed after publishing, and applies to attempts already made):

| Setting | The answers are shown |
|---------|-----------------------|
| Right after they submit (the default, and how every exam behaved before) | as soon as an attempt is submitted. Someone who finishes early can pass the answers on to candidates still sitting the exam, so the page says so. |
| From a set time | from that time, for example once the whole exam window has closed. |
| When I release them | when an administrator presses **Release answers now** on the published exam. Releasing twice keeps the first time; switching the setting resets it. |

| What | Rule |
|------|------|
| Where the key is sent | Only by `GET /v1/me/attempts/{id}/review`. Nothing shown while sitting the exam carries it, and a test fails if a property naming it is ever added to those types. |
| Refused | `404` for anyone but the attempt's owner (an administrator too); `409 attempt_not_submitted` while the attempt is open; `409 results_not_released` before the answers are released, saying from when if that is known. The answer key is not even read in those cases. |
| One rule | released = "Instant, or the release time has arrived". A manual release just sets that time to now. |
| Permissions | Setting and releasing need `exam.manage`; reading a review needs only being the candidate who sat the attempt. |

The setting governs the answer review only: the score is still shown as soon as an attempt is submitted.

#### Answer-key corrections (FR-31)

A question's answer key is otherwise locked once candidates have answered it (only wording can change), but a staff member holding `question.manage` can correct it on purpose — the deliberate exception for when the key itself turns out to be wrong, not an author's edit. `POST /v1/questions/{questionId}/correct-answer-key` with `{ "correctOptionIds": [...], "reason": "..." }` replaces which options are correct, leaving the text, order and pinning of every option untouched, and rescores every already-submitted attempt that included the question (whether the candidate answered it or it was only drawn onto their paper) under the corrected key. The response is `{ "keyChanged": bool, "attemptsRescored": int }`; naming the key's current correct options back changes nothing and rescores nothing.

A rescored attempt's new score replaces its old one, but the change is kept as a revision (previous score, new score, the reason, when), not applied silently: the candidate's answer review lists every revision the attempt has had. The correction is written to the admin audit log (`QuestionBank.AnswerKeyCorrected`) with the reason and how many attempts it rescored, not the attempts themselves. There is no staff page for this yet (API only); raising a dispute is likewise not built — a candidate who thinks a key is wrong still has to tell staff out of band.

#### Bulk import/export (FR-6)

A staff member holding `question.manage` can create questions from a CSV file, or download questions as one, from `/v1/questions`. The two share one column shape, so a bank's own export is always a file it can re-import unchanged: `Text,Option1,Correct1,...,Option6,Correct6,AllowsMultiple,Difficulty,Topics` (up to six options as fixed column pairs; an unused pair is left blank, so a two-option question's later columns are simply empty; `Topics` is `;`-joined since `,` is the column delimiter). Chapter placement is not part of the file — it stays the separate `POST /v1/questions/placement` bulk action, so a row never has to resolve a chapter by name across books.

| What | Rule |
|------|------|
| Importing | `POST /v1/questions/import` with `{ "csv": "..." }`. Every row is checked under the exact rules the single-question form enforces (text required and sanitized, 2–6 options, exactly one correct unless `AllowsMultiple`), and a bad row is reported and skipped rather than failing the whole file: the response is `{ "created": [{ "row", "id" }], "rejected": [{ "row", "errors" }] }`, row numbers counting the header as line 1. One save covers every row that validated, so a later row's rejection can never undo an earlier row's creation. At most 1000 data rows per call; a larger file is refused outright with `400 bulk_import_too_large` before any row is read. |
| Exporting | `GET /v1/questions/export`, with the same filters as `GET /v1/questions` (`bookId`, `chapterId`, `unfiled`, `difficulty`, `topic`, `q`). Returns the matching questions as a CSV file (`questions.csv`), newest first, up to 5000 rows; a larger bank needs a narrower filter to export it in parts. |
| Permissions | Both routes need `question.manage`, the same as every other question-authoring action. |

There is no staff page for this yet (API only, so a file is imported or exported with a raw request for now), and CSV is the only format supported — no Excel or JSON.

#### Question versioning (FR-7)

Every accepted change to a question's gradable content — creating it, a successful `PUT /v1/questions/{questionId}` edit, or an answer-key correction — takes a new, numbered version: a snapshot of the text, options and answer key at that moment. A version already taken is never edited or removed, so `GET /v1/questions/{questionId}/history` always reads the same for an earlier version no matter what the author does afterward. A rejected edit (one the rules in [Editing, deleting and filing questions](#editing-deleting-and-filing-questions) refuse) takes no version, since nothing changed; neither does classifying a question's difficulty or topics, or filing it under a chapter, since neither reaches a candidate or affects marking.

`GET /v1/questions/{questionId}/history` (needs `question.manage`, like every other question-authoring route) returns every version oldest first: `[{ "versionNumber", "text", "options": [{ "id", "text", "isCorrect", "isPinned" }], "allowsMultiple", "createdAtUtc" }]`. This is history for staff to review, not the versioning the requirements' data model describes for exams: an exam still references a question by its id directly, and reads whatever the question's current content is (own key rule: only the wording of a question's text and options can change once candidates have answered it, and an answer-key correction is rescored separately — see [Answer-key corrections (FR-31)](#answer-key-corrections-fr-31)), rather than pinning each exam question to one immutable version id. There is no staff page for this yet (API only), and no way to revert to an earlier version — the history is read-only.

#### Extra attempts

Every candidate has **one attempt** at an exam unless its author allows more (see [Attempts allowed](#attempts-allowed) below). When a candidate asks for another (a power cut, a dropped connection), an administrator gives them one on **Candidates and attempts** (`/exams/:id/attempts`, linked from a published exam; needs `exam.manage`). The candidate then sees "Start attempt 2" on My exams, and each attempt is numbered, scored and reviewed on its own. My exams lists every attempt and marks the highest-scoring submitted one as **Best** once there are two to compare (the earlier one on a tie); nothing is stored as the exam's official score.

| What | Rule |
|------|------|
| Allowance | The exam's attempts allowed (1 unless the author chose more) + the extra attempts granted. A grant is a row recording who allowed it, when and why (an optional reason), not a counter. |
| When it can be given | The candidate is enrolled, the exam can still be started (not past its window or its late-entry cutoff, because the attempt could never be used), and they have **used exactly the attempts they hold**. Not fewer, which stops a double click, or two administrators, from stockpiling attempts; and not more, which only a lowered limit can cause (see below). |
| Starting | An open attempt is resumed. With none open and one left, the next begins under the same window rules, with a fresh deadline that never passes the window's end. With none left, starting just returns the latest result. |
| Races | Settled by the database: attempts are unique on (exam, candidate, number) and grants on (exam, candidate, grant number), so the loser of two simultaneous requests gets a 409 rather than a second sitting or a second grant. |
| Shuffling | By default the first attempt shows questions and options as the author wrote them, and **every later attempt shows them shuffled**. The author can also turn shuffling on for the questions, the options, or both from a "Shuffling" card on the exam page (`PUT /v1/exams/{id}/shuffle` with `shuffleQuestions` and `shuffleOptions`, a missing flag is `400 invalid_exam_config`); that shuffles the first attempt too. It can be changed on a draft only (`409 exam_not_draft` once published), because the order is worked out from these settings every time an attempt is read, so a later change would move questions under candidates already sitting or reviewing. The flags were already stored but unused, and stored as on for every exam, so a data-only migration (`ShuffleOffByDefault`) sets them off for existing exams and a new exam starts with both off; that keeps every exam behaving as before until its author turns shuffling on. What is shuffled: the questions within each section (sections keep their order) and the options of each question. The shuffle is a function of the attempt (SHA-256 over the attempt, the list and the item), so a reload, a resume on another device and the review all show the order the candidate originally saw; it always differs from the authored order, and with two options they are swapped. Answers are saved by id, so the score does not depend on the order. |
| Pinned options | In the question editor each option has a **Keep in place** box. A pinned option (a "none of the above" that must stay last) keeps the position the author gave it in every shuffled attempt; the other options are shuffled among the positions left. Scoring is unaffected. Once candidates have answered, which options are pinned is locked with the rest of the key (`409 question_locked`), because it decides the order a review must reproduce. Migration `PinnedOptions` adds a `boolean` column `IsPinned` (default false) to `questionBank.QuestionOptions`, so no existing question changes. Requests may send `isPinned` on an option; omitted means not pinned. |
| Release setting | The answer-review setting applies per exam, not per attempt. With "right after they submit", a candidate who is given another attempt has already seen the correct answers. The exam page's **Attempts allowed** card warns about this when the limit is above one; a single extra attempt granted on an exam with a limit of one does not show that warning, so the author decides with it in mind. |

**Asking for one.** Once a candidate has used every attempt they hold (and none is open, and the exam can still be started), **My exams** offers "Ask for another attempt" with an optional reason. The request waits in the queue at **Attempt requests** (`/admin/attempt-requests`, `exam.manage`), oldest first. **Approving** records the grant and marks the request approved in one save, so the two cannot disagree; **declining** can give a reason, which the candidate then sees, and they may ask again. A candidate has at most one waiting request per exam (a partial unique index, so two taps cannot queue two), and a decided request cannot be decided again. **Telling staff.** When a request is recorded, every active user who holds `exam.manage` through any role (found through the new `IStaffDirectory` contract in Identity) is e-mailed with the candidate, the exam and the reason; the request is saved first and stays in the queue whatever happens to the e-mails, and nothing is sent if nobody holds the permission or no mail server is set up. The candidate is e-mailed the answer (and the reason, when declining) through the same `Smtp` settings as invitations; the response carries `candidateNotified`, and the page says plainly when they could **not** be e-mailed (no mail server, the server refused, or they are no longer enrolled) so the administrator tells them. A mail failure never undoes the decision. If an administrator gave the attempt directly in the meantime, approving answers 409 `attempt_available`, and the request can be declined. Routes: `POST /v1/me/exams/{id}/attempt-requests` (409 `attempt_not_needed`, `attempt_request_pending`, `exam_closed`, `attempt_over_limit`; 404 `exam_not_available`), `GET /v1/attempt-requests?status=` (waiting by default), `POST /v1/attempt-requests/{id}/approve` and `/decline` (409 `attempt_request_not_pending`, 404 `attempt_request_not_found`).

Routes: `GET /v1/exams/{id}/attempts` and `POST /v1/exams/{id}/candidates/{candidateId}/extra-attempts` (409 `attempt_available` when they still have an attempt, 409 `attempt_over_limit` when the limit was lowered below what they made, 409 `exam_closed`, 404 `candidate_not_enrolled`). The `MultipleAttempts` migration numbers every existing attempt 1 and was checked on a database that already held attempts; rolling it back refuses, rather than lose attempts, once a candidate has used an extra one.

#### Attempts allowed

The exam's author chooses how many attempts **every** enrolled candidate has, on the exam page (`/exams/:id`, the **Attempts allowed** card; needs `exam.manage`). An exam gives one attempt until the author says otherwise, so every exam made before this setting existed behaves as it did.

| What | Rule |
|------|------|
| Setting | `PUT /v1/exams/{examId}/attempt-limit` with `{ maxAttempts }`, a whole number from 1 to 10 (`400 invalid_exam_config` for anything else, or for a body with no number). It returns the exam, whose `config.maxAttempts` carries it. The value was already stored on the exam (`Config_MaxAttempts`, default 1), so **there is no migration**. |
| When it can change | While the exam is a draft **or published** (not archived, which is refused), like the answer-review setting: it changes nothing that is asked or scored. |
| What it means | Attempts every candidate has before an administrator gives anyone an extra one. Extra attempts still add on top, for that candidate alone: allowed = the exam's limit + their grants. |
| Raising it | Opens the extra attempts for every candidate at once. A candidate who finished their attempts can start the next one straight away, under the same window and late-entry rules as any attempt. |
| Lowering it | Takes nothing back: attempts already made stay, are scored and reviewed as before, and an attempt in progress can still be resumed and submitted. It only stops further ones. A candidate who is now **over** the limit (more made than allowed) cannot start another, and an extra attempt is **refused for them** (`409 attempt_over_limit`), because one more would still leave them over it and change nothing; the page says to raise the exam's limit instead. |
| The runtime | `ExamSnapshot.MaxAttempts` carries the number across the module boundary, and `AttemptAllowance` (one pure class) decides who may start or be granted another attempt from it, so starting, granting, My exams and the staff page can never disagree. |
| Not here | Per-candidate or per-batch limits (only the one-at-a-time grants above), a best-of or last-of scoring policy, a wait between attempts, and `ExamConfig.MaxRetakes`, which overlaps with this number and stays unused. |

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

#### Editing, deleting and filing questions

Questions can be edited, deleted and filed under a book and chapter from `/admin/questions` (the `question.manage` permission). Exams and attempts read a question's text and options live from the bank, and a saved answer points at an option's id, so what may change depends on where the question is in use. The bank asks every module that uses questions and the list shows the answer as badges on each question: **In N exams** (hover for their names) and **Answered by candidates**.

| What | Rule |
|------|------|
| Edit | `PUT /v1/questions/{id}` (page: `/admin/questions/:id/edit`) takes the question's whole new content under the same rules as creating one: the same sanitizing, limits and exactly one correct option. **Before a candidate has answered it, everything can change.** Options are matched by id, so an option that is kept keeps its identity. **Once a candidate has answered it, only the wording can change**: the same options, in the same order, with the same one correct. Anything else is `409 question_locked` and changes nothing, because stored scores were worked out against that key and a review is worked out against it again. To change the answers, create a new question. |
| Delete | `DELETE /v1/questions/{id}`. Refused with `409 question_in_use`, naming the exams, while any exam (draft or published) holds the question. Every answered question is in an exam, so this also covers "attempted". The card shows Delete disabled, with the reason, instead of letting you click and be refused. To delete a question that a **draft** exam holds, take it out of that exam first (see [Putting a draft exam right](#putting-a-draft-exam-right)). |
| File under | `POST /v1/questions/placement` with `{ questionIds, chapterId }`, so filing one question and filing a page of them are the same call. Each question has **File under…**, and each has a tick box; **Select all shown** and the bar above the list file the ticked ones together (the usual job: filter to "Not filed under a chapter", select all, choose a chapter, file). It is all or nothing, and a question already in that chapter is not counted as moved. The chapter must be open (`404 chapter_not_found`, `409 book_archived`). Filing never changes a question's content or its attempts. |
| Exams limited to a book or chapters | A **draft** exam whose scope would no longer hold a question it contains objects to the move (`409 placement_refused`, naming the exam, and nothing moves). Published exams are not checked: their scope is fixed and exam delivery never reads where a question is filed. Unfiled questions can never be in a scoped exam, so filing the unfiled ones is never refused. |
| Where the rules live | The bank must not depend on the modules that use its questions, so it defines `IQuestionUsageSource` and `IQuestionPlacementGuard` in its Contracts project and each module contributes an implementation: ExamAuthoring says which exams hold a question and guards the scope rule; ExamRuntime says which questions candidates have answered. A source that fails fails the request, so a question is never taken for unused by mistake, and integration tests fail if a module's source is not registered. |
| Migrations | `QuestionUsageIndex` (exams) and `QuestionAnsweredIndex` (runtime) each only add an index, so "which exams hold this question?" and "has anyone answered it?" do not scan a table. |

#### Putting a draft exam right

While an exam is a draft, mistakes can be corrected from its page (`/exams/:id`, the `exam.manage` permission). Once it is published, candidates may have been invited to it or sat it, and its questions are what their scores mean, so everything below except the name and description is refused (`409 exam_not_draft`).

| What | Rule |
|------|------|
| Take a question out | `DELETE /v1/exams/{examId}/sections/{sectionId}/questions/{questionId}`, where the id is the question's id in the bank, the one it was added with. The question stays in the bank, the questions after it are renumbered so there is no gap, and it can be added again. A question the section does not hold is `404 question_not_in_exam`. This is also how a question added by mistake becomes deletable from the bank again. |
| Remove a section | `DELETE /v1/exams/{examId}/sections/{sectionId}`. Its questions leave the exam with it (they stay in the bank) and the sections after it are renumbered. The page asks first and says how many questions go with it. |
| Rename a section | `PUT /v1/exams/{examId}/sections/{sectionId}` with `{ name, timeSeconds }`. It replaces both values, so the page sends the section's current time limit back with the new name; renaming never removes a limit. Draft only, because a time limit decides how long a candidate has. |
| Correct the name and description | `PUT /v1/exams/{examId}/details` with `{ name, description }`: a name of 1 to 255 characters, a description of up to 1000, and a blank description clears it (`400 invalid_exam_config` otherwise). **Allowed on a published exam too** (not an archived one), because it changes nothing that is asked or scored. |
| Delete the draft | `DELETE /v1/exams/{examId}` (204). Only a draft, and only while nothing refers to it (`409 exam_not_deletable`, with every reason). The exam builder defines `IExamDeletionGuard` in its Contracts project and each module that keeps an exam's id contributes one: Invite objects while an invitation is waiting or accepted (revoke it first; revoked, declined and expired ones do not count), and Batch objects while a batch is assigned. The exam is marked deleted, as every module's records are, so every query leaves it out; its questions stay in the bank. A guard that fails fails the delete, so an exam is never deleted because a module could not be asked, and an integration test fails if a module's guard is not registered. |
| Migrations | None: these only change rows the tables already hold. |

#### Instructions and system check

Pressing **Start exam** on `/my-exams` opens `/my-exams/{examId}/start` first (FR-17); no attempt exists until the candidate leaves it.

| What | Rule |
|------|------|
| Instructions | Written from the exam's own settings, so only rules that are true of it appear: the time allowed and that the timer runs on the server (it keeps running through a refresh or a lost connection, and the exam is submitted for them when time runs out), that answers save as chosen, the marking scheme (correct, wrong and unanswered, and partial credit when the exam has it), whether sections must be taken in order, that a second sign-in ends the session, and which attempt this is when there are several. `GET /v1/me/exams` carries the rules (`rules`). |
| System check | Runs on arrival and on **Run the check again**. **Browser**: fetch, structuredClone, Intl and writable site storage. **Internet connection**: online or not. **Exam server**: three timed requests to `/v1/health`, judged by the median so one slow answer (a server waking up) does not decide it. **Connection speed**: the browser's reported downlink when it has one (not every browser does, then it says so and does not block). |
| What blocks | A **problem** (an old browser, blocked storage, offline, or the server cannot be reached) disables Start until it is fixed. A **warning** (a slow server or connection) never blocks: the exam sends little data, so the page says the exam may only feel slower. |
| Acknowledgment | Start stays disabled until the candidate ticks "I have read and understood these instructions". The server enforces it too: `POST /v1/me/exams/{examId}/attempts` takes `{ "instructionsAcknowledged": true }`, and a new attempt without it is `400 instructions_not_acknowledged` and creates nothing. Resuming an open attempt, or learning that none is left, needs none, so those calls stay safe to repeat. |
| Record | The attempt stores when the instructions were acknowledged (`InstructionsAcknowledgedAtUtc`, the same instant as the start), so a dispute can show the candidate was told the rules. Attempts begun before this change have none. |
| Not built | Camera and microphone checks: they are only asked for when an exam's proctoring profile needs them (FR-46), and there are no profiles yet. Instruction text the exam's author writes, and per-institute templates (FR-41), are not offered; the instructions are generated. |
| Migration | `AttemptInstructionsAcknowledged` adds one nullable column to `examRuntime.Attempts`. |

#### Copy, paste, right-click and print protection

While an exam is open, the exam page turns off the ordinary ways of carrying a question out of it (FR-23).

| What | Rule |
|------|------|
| What is turned off | Copy, cut and paste (the keyboard chords, including Ctrl or Cmd with C, X, V, and Ctrl+Insert, Shift+Insert, Shift+Delete, and the menu entries), the right-click menu, and dragging content off the page. A candidate who tries sees one calm line ("Copying is turned off during this exam."), announced politely to screen readers, which goes away after a few seconds. |
| Printing | Ctrl or Cmd with P is refused. A browser's own Print menu cannot be cancelled by a page, so while the exam is open the page's print stylesheet makes a printout blank except for a sentence saying printing is turned off. |
| What is left alone | Selecting text, so screen readers and magnifiers keep working; Ctrl+A and Ctrl+F; the exam shortcuts (N, P, M, C); the browser's inspector. Candidates are told the rule on the exam page and in the instructions before they start. |
| It is a deterrent | It stops the ordinary routes. It cannot stop a photograph of the screen, a second device, or a browser set up to ignore a page, which is what proctoring (M6) is for. |
| Per exam | `ContentProtection` is on for a new exam and for every existing one. An author with `exam.manage` can turn it off, for example for practice and chapter tests, with `PUT /v1/exams/{examId}/content-protection` (`{ "contentProtection": false }`; a body without the flag is a `400`), from the exam editor's **Copying and printing** card. Like the attempt limit it can change after publishing, since it changes nothing that is asked or scored; an attempt in progress picks the change up when its page is next loaded. The exam's config and the candidate's attempt both report it (`contentProtection`). |
| Only while the exam is open | The guard is on only while an attempt is in progress, and is released the moment it is submitted or the page is left, so a candidate can copy and print their own result and review. |
| Not built | A proctoring profile that bundles this with fullscreen and tab-switch rules (requirements section 8, FR-46): when those exist, this setting becomes the profile's `blockClipboard`. Logging refused attempts (FR-26). |
| Migration | `ExamContentProtection` adds `Config_ContentProtection` to `examAuthoring.Exams`, defaulting to true for existing exams. |

#### Leaving the exam page: warnings and a violation limit

While an exam that watches for it is open, the page reports each time the candidate leaves it, warns them, and the server ends the attempt when they have left too often (FR-22).

| What | Rule |
|------|------|
| What counts | The tab is hidden (switching tab, minimising), the window loses focus (clicking into another application), or full screen is left. Switching tab fires both "hidden" and "blur", so one trip away is one departure: the next is counted only once the candidate is back. Leaving full screen counts only after they were in it, and the page offers an **Enter full screen** button where the browser allows it. |
| Per exam | `FocusViolationLimit`, from 0 to 20, where 0 (the default, for new and existing exams) means the exam does not watch. Ending somebody's sitting is a proctoring choice an author makes on purpose, so nothing starts doing it until they ask. An author with `exam.manage` sets it with `PUT /v1/exams/{examId}/focus-violation-limit` (`{ "focusViolationLimit": 3 }`; a missing or out-of-range number is a `400`) from the exam editor's **Leaving the exam page** card. Like the attempt limit it can change after publishing, since it changes nothing that is asked or scored. |
| The server holds the count | `POST /v1/me/attempts/{attemptId}/focus-violations` with `{ "kind": "TabHidden" \| "WindowBlurred" \| "FullscreenExited" }` records one departure, with the **server's** clock, as a row in `examRuntime.AttemptFocusViolations`, and answers `{ violations, limit, attemptEnded }`. Reloading the page cannot reset the count: the attempt reports `focusViolationLimit` and `focusViolations`. A kind that is not one of the three names is a `400`; someone else's attempt is a `404`; a submitted one is a `409`. An exam that does not watch records nothing and answers `limit: 0`. |
| At the limit | The departure that reaches the limit scores what was saved and closes the attempt in the same save as the record, so an attempt never ends without the evidence that ended it. Such an attempt is `autoSubmitted` and `endedByViolations`, at the moment it happened, and the result page says why. An administrator can then give the candidate another attempt, as for any other. |
| What the candidate sees | Told up front on the instructions page, with the limit. During the exam a line says how many of the allowed departures they have used; after each one an alert says "You left the exam page (1 of 3 allowed). If you leave 2 more times, the exam will be submitted." and stays until dismissed, because they have just come back and must see it. |
| It is a deterrent and a record | The page can only report what the browser tells it. A browser set up to hide it, or a second device, cannot be detected, and a departure while the candidate is offline is not heard by the server, so it is not counted; the page drops a report it cannot send rather than nagging. Nothing reads the count as proof, and nothing but the exam's own limit acts on it. |
| Not built | A proctoring profile that bundles this with copy protection and consent text (FR-46); admin warnings, pausing and invalidating an attempt (FR-29); the risk score (FR-27); and showing reviewers the list of departures (the rows are stored for it). |
| Migrations | `ExamFocusViolationLimit` adds `Config_FocusViolationLimit` to `examAuthoring.Exams` (default 0). `AttemptFocusViolations` adds `examRuntime.AttemptFocusViolations` and `Attempts.EndedByViolations` (default false). |

#### Sitting an exam: clearing a response and marking for review

Two controls under each question on the exam page (`/attempt/:id`), as on a printed paper: **Clear response** takes the chosen option back, and **Mark for review** is a note to come back to the question. Both show at once and are saved in the background; if a save fails the page puts things back as they were and says so.

| What | Rule |
|------|------|
| Clear response | `DELETE /v1/me/attempts/{attemptId}/answers/{questionId}` (204). The saved answer is deleted, so the question is unanswered again and earns the exam's unattempted marks (none by default). It is deleted rather than blanked so that everything that reads answers (the scorer, the review, the check that keeps a question's answer key fixed once answered) still means "a candidate chose this option": taking back the only answer to a question frees its answer key to change again. Clearing a question with no answer does nothing and is not an error. |
| Mark for review | `PUT /v1/me/attempts/{attemptId}/marks/{questionId}` marks and `DELETE` on the same path unmarks (204 either way; repeating one changes nothing, so a retry after a dropped connection is safe). It works on answered and unanswered questions alike. A mark is kept in its own table (`examRuntime.AttemptMarks`, one row per question per attempt) because it is independent of an answer, and nothing that scores or reviews an attempt reads it: **a mark never changes the score**. |
| When | Only while the attempt is open: a submitted attempt answers `409 attempt_not_in_progress`, and one whose time has run out is closed with what was saved, exactly as for an answer. |
| Which questions | Only questions of the exam being sat (`404 question_not_in_attempt`). Someone else's attempt looks like a missing one (`404 attempt_not_found`). |
| The palette | Five states: not visited, not answered, answered, marked for review, and answered and marked. Each reads in words for a screen reader ("Question 4, answered and marked for review") and the look never relies on colour alone: a dashed edge for a question seen but not answered, a corner dot for a marked one. The count line and the "Submit now?" question say how many are marked, so none is forgotten. |
| Before submitting | "Submit exam" asks first. The question says how many questions are answered, how many are marked for review and how many were never opened ("not visited", by the palette's own rule, so the two agree). With more than one section it also lists each section's answered, marked and not visited counts. Nothing here is sent to the server; it is read from the page's own state. |
| What is remembered | Answers and marks are stored, so a reload or a resumed exam shows them. "Not visited" is kept in the browser, per attempt: it survives a reload but not a change of device, where a question that is neither answered nor marked reads "not visited" again. |
| Migration | `AttemptMarks` adds one table; no existing row is touched. |

#### Sitting an exam: Save & Next, text size and section lock

| What | Rule |
|------|------|
| Save & Next | Moves to the next question. An answer is saved the moment it is chosen, so the button has nothing left to send; it is there for candidates used to "save, then next". On the last question it does nothing. |
| Text size | **A−** / **A+** on the exam page step the question text through 100, 115, 130 and 150 percent. The palette and timer keep their size. The choice is remembered in the browser only (it is a comfort setting, not part of the exam). Dark mode follows the system setting. |
| High contrast | A **High contrast** button on the exam page switches to a black, white and yellow scheme whatever the system theme; it is remembered in this browser. States stay distinguishable by border style and the corner dot, not colour alone. |
| Keyboard shortcuts | **N** next, **P** previous, **M** mark for review, **C** clear response. Presses with Ctrl, Alt or Meta are left to the browser, and the shortcuts pause while a confirmation is open. |
| Section lock | Turned on per exam (FR-12). While on, a candidate is in one section and may only answer, clear or mark questions in it: the server answers `409 section_locked` to any other, so the palette's greyed-out sections are a courtesy and not the rule. Moving on is `PUT /v1/me/attempts/{attemptId}/section/{sectionId}` (204; `404 section_not_in_attempt` for a section of another exam; `409 section_locked` for going back). The page asks "You will not be able to come back" first. The section a candidate is in is stored on the attempt (`ActiveSectionOrder`, only ever increases), so a reload or a replayed request cannot reopen a section left. Moving to a later section skips the ones between for good. Exams without the lock ignore all of this. |
| Not built | A per-section timer (FR-12) and returning to a locked section are not offered. |
| Migration | `AttemptActiveSection` adds one integer column to `examRuntime.Attempts` (default 1). |

**Known gaps**: no background worker, so abandoned attempts close lazily; the exam page has no accessibility options beyond text size, high contrast and the four shortcuts (no screen-magnifier layout, no extra time), and "not visited" is not carried to another device; a candidate asks for another attempt in the app and an administrator answers in a queue; the candidate is e-mailed the answer (when `Smtp:Host` is set), and everyone who holds `exam.manage` is e-mailed when a candidate asks (who asked, for which exam, and why); multiple choice only (one or several correct options, but no other question types), where a multiple-answer question earns marks only for exactly the right set unless the author turns on partial credit in the marking scheme (then each correct option chosen earns its share of the correct-answer marks and each wrong one takes a share away, never below the incorrect-answer marks; the setting is fixed once the exam is published, and the review shows such a question as "partly correct"); candidates do not see books or chapters, and an exam draws questions by rule only when the author asks: "Add random questions" fills a draft section once, with questions picked at random now (the exam keeps that fixed list, so every candidate sits the same questions), while "Draw again for each candidate" adds a rule instead: when a candidate starts an attempt the server draws that many matching questions for them alone, on top of the section's fixed ones, stores the paper so a reload or resume shows the same questions, and draws afresh for every later attempt; staff can open any attempt's paper from the exam's attempts page (drawn questions are marked, `GET /v1/exams/{examId}/attempts/{attemptId}/paper`, `exam.manage`); a rule needs enough matching questions (publishing is refused otherwise, and a start is refused if the bank has since shrunk), rules of one section are only checked one at a time at publish, rules cannot be changed once the exam is published, and a drawn question counts as used, so its answer key cannot change afterwards except through the staff answer-key correction (FR-31), which rescores every attempt it affects; chapters cannot be reordered, and a question sits in one chapter only, and cannot be taken out of its chapter again (it can be moved to another); the list can be searched by the words in a question or in any of its options (case-insensitive, a plain substring, not word-aware or ranked; it reads a plain-text copy kept with each question, filled in for older questions by a migration that only strips tags, so an older question is indexed exactly once it is next edited), and a question can carry a difficulty (easy, medium, hard) and up to five free-text topics, and the list can be filtered by either, but there is no topic list to curate (a topic exists while some question uses it), no rename of a topic across questions; "Exam Series" is still only an optional ID with no entity behind it (a book is not a series); the question list loads 200 at a time and has no total count (it shows a "Load older questions" button while a page comes back full, so an exact multiple of 200 shows the button once more and then finds nothing); no tables, links, math or alt-text prompt in question text, and option text is plain; marking uses the exam's marking scheme (default +1 / 0 / 0) and the score is shown immediately whatever the answer-review setting; there are no written explanations per question (the question bank has no such field), and a review shows the question as it is now, which is why only the wording of a question can change once candidates have answered it (there are no stored versions of a question, FR-7); a question that is in an exam can be deleted only after it has been taken out of that exam, which only a draft allows (a published exam keeps its questions for good); a published exam cannot be withdrawn, archived or deleted; two authors editing the same question at once is last write wins; options cannot be reordered in the editor (the API accepts a reordering of a question nobody has answered); batches do not yet feed enrollment; guardian verification is not built (its route was removed rather than left answering 500), and guardian records are staff-managed; e-mail needs a real SMTP server to be tried.

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

A step-by-step guide for hosting the API and site on Render's free plan, with a Neon database, including moving an existing local database, is in [docs/deploy-render.md](docs/deploy-render.md).

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
- **Commands and queries**: one plain handler class per use case (`HandleAsync`), resolved by the endpoints; MediatR is not used (ADR 0001)
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

### Errors, paging and the audit trail

| What | Rule |
|------|------|
| Errors | A refused request answers `application/problem+json`. `title` is a stable error code (`batch_not_found`, `duplicate_member`, `invalid_page_request`, ...), `detail` says what is wrong, and `traceId` is the request's trace id, the same one its logs carry, so quote it when reporting a failure. Unknown ids are 404, a state that does not allow the action is 409, bad input is 400. |
| Conflicts | Two requests racing for the same thing do not both win. Accepting an invite code twice at once, or giving one address two seats in a batch, answers the loser `409` (`concurrency_conflict`, `duplicate_member`). |
| Batch members | An address is trimmed and lower-cased before it is stored, so `A@x.com` and `a@x.com` are one seat. A phone number may contain spaces and dashes; it is stored without them (an optional `+`, then 7 to 15 digits). A bad address or number is `400 invalid_batch_member`. |
| Paging | A paged list takes `page` (1 to 1,000,000, default 1) and `pageSize` (1 to 200, default 50); anything else is `400 invalid_page_request`. Today that is `GET /v1/admin/audit-logs`. |
| Audit trail | Creating and publishing an exam, every batch change (created, member added or removed, activated, closed) and every invite change (created, accepted, declined, revoked) is recorded, with who did it (from the access token, so for an accept it is the candidate) and the request's trace id. Entries name members and invites by id and never hold an e-mail address. Read it with `GET /v1/admin/audit-logs` (`admin.audit.read`). A change to an exam other than creating and publishing it is not audited yet, and neither are guardian changes. |

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
GET    /v1/admin/otp-codes            (SuperAdmin: unspent candidate sign-in/registration codes, audited)
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

**Last Updated:** 2026-10-02 | **Status**: M1 complete; the M3 exam loop (question → exam → invite → take → score) works end to end, with the gaps listed above
