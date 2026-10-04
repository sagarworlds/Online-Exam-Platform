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
7. **My exams** (`/my-exams`): Start (the server fixes the deadline), answer (saved as they go; an answer can be cleared and a question marked for review, see [Sitting an exam](#sitting-an-exam-clearing-a-response-and-marking-for-review)), submit, and read the score, then review which answers were right once they are released. If time runs out the attempt is closed with the saved answers the next time anyone looks at it. A candidate asks for another attempt by contacting an administrator, who gives one on the exam's **Candidates and attempts** page (see [Extra attempts](#extra-attempts)).

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

**Known gaps**: no background worker, so abandoned attempts close lazily; the exam page has no accessibility options beyond text size, high contrast and the four shortcuts (no screen-magnifier layout, no extra time), and "not visited" is not carried to another device; a candidate asks for another attempt in the app and an administrator answers in a queue; the candidate is e-mailed the answer (when `Smtp:Host` is set), and everyone who holds `exam.manage` is e-mailed when a candidate asks (who asked, for which exam, and why); multiple choice only (one or several correct options, but no other question types), where a multiple-answer question earns marks only for exactly the right set unless the author turns on partial credit in the marking scheme (then each correct option chosen earns its share of the correct-answer marks and each wrong one takes a share away, never below the incorrect-answer marks; the setting is fixed once the exam is published, and the review shows such a question as "partly correct"); candidates do not see books or chapters, and an exam draws questions by rule only when the author asks: "Add random questions" fills a draft section once, with questions picked at random now (the exam keeps that fixed list, so every candidate sits the same questions), while "Draw again for each candidate" adds a rule instead: when a candidate starts an attempt the server draws that many matching questions for them alone, on top of the section's fixed ones, stores the paper so a reload or resume shows the same questions, and draws afresh for every later attempt; staff can open any attempt's paper from the exam's attempts page (drawn questions are marked, `GET /v1/exams/{examId}/attempts/{attemptId}/paper`, `exam.manage`); a rule needs enough matching questions (publishing is refused otherwise, and a start is refused if the bank has since shrunk), rules of one section are only checked one at a time at publish, rules cannot be changed once the exam is published, and a drawn question counts as used, so its answer key cannot change afterwards; chapters cannot be reordered, and a question sits in one chapter only, and cannot be taken out of its chapter again (it can be moved to another); the list can be searched by the words in a question or in any of its options (case-insensitive, a plain substring, not word-aware or ranked; it reads a plain-text copy kept with each question, filled in for older questions by a migration that only strips tags, so an older question is indexed exactly once it is next edited), and a question can carry a difficulty (easy, medium, hard) and up to five free-text topics, and the list can be filtered by either, but there is no topic list to curate (a topic exists while some question uses it), no rename of a topic across questions; "Exam Series" is still only an optional ID with no entity behind it (a book is not a series); the question list loads 200 at a time and has no total count (it shows a "Load older questions" button while a page comes back full, so an exact multiple of 200 shows the button once more and then finds nothing); no tables, links, math or alt-text prompt in question text, and option text is plain; marking uses the exam's marking scheme (default +1 / 0 / 0) and the score is shown immediately whatever the answer-review setting; there are no written explanations per question (the question bank has no such field), and a review shows the question as it is now, which is why only the wording of a question can change once candidates have answered it (there are no stored versions of a question, FR-7); a question that is in an exam can be deleted only after it has been taken out of that exam, which only a draft allows (a published exam keeps its questions for good); a published exam cannot be withdrawn, archived or deleted; two authors editing the same question at once is last write wins; options cannot be reordered in the editor (the API accepts a reordering of a question nobody has answered); batches do not yet feed enrollment; the guardian verification path is still a stub; e-mail needs a real SMTP server to be tried.

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
GET    /v1/admin/otp-codes            (SuperAdmin: unspent candidate sign-in/registration codes, audited)
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
