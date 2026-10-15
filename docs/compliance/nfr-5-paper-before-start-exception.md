# Exception: the question paper is not encrypted until the exam starts

**Requirement:** NFR-5, security: "question paper encrypted until exam start" (see `exam-platform-requirements.md`, NFR-5 and section 15, M8).
**Issue:** [#86, NFR-5 security test](https://github.com/sagarworlds/Online-Exam-Platform/issues/86).
**Decision:** accepted by the product owner on 2026-10-15, for the pilot. Candidates are kept from the paper until the exam starts by access control, and not by a key that is released at the start time.
**Review:** reviewed by no one but the product owner. This is a security control and not a legal one, so it does not wait for counsel. A security reviewer should assess it, and the penetration test (action A4 in `docs/owner-decisions.md`) is the place to ask.

## What the requirement asks for, and what the platform does

The requirement asks for the paper to be unreadable until the start time and readable after it. The platform has no such step. Question content is encrypted at rest (`QuestionContentCipher`, #215), and the server can open any question at any time.

What keeps the paper from candidates is that nobody has it to read:

- **A candidate has no attempt before the window opens.** `StartAttemptHandler.Begin` throws `ExamNotOpenError` while the time is before the exam's start (`StartAttemptHandler.cs:129-130`), and the paper is drawn only after that call (`:99-100`). The unit test `Start_BeforeTheWindowOpens_IsRefused` pins it.
- **No candidate route returns questions without an attempt.** This was checked in the audit of 2026-10-12 (see `nfr-5-security-checks.md`). It has not been re-checked for routes added since.
- **A copy of the database does not open the paper.** The questions are encrypted, and the key ring that decrypts them is itself encrypted under a key held outside the database (#86, `docs/adr/0003-key-ring-protection.md`). This protects the stored copy. It does not protect the running application, or anyone who holds both the database and the key.

## Who can read the paper before the start

Staff can, and this is by design:

- **The preview route.** `GET /v1/exams/{examId}/preview` needs the `exam.manage` permission (Super Admin and Exam Admin). It shows the exam as a candidate would see it, in any state. It returns no answer keys, and it stores nothing (FR-15).
- **The question bank.** Staff with the `question.read` permission (Super Admin, Exam Admin, Content Author and Reviewer) read every question at any time, because writing and reviewing questions is their job. An exam's paper is made of questions from the bank, so a release step at the start time would not hide the questions from these roles.
- **Whoever holds the database and the key.** The key sits in Render's environment, so a person with access to the Render dashboard and to the Neon database can open the content.

## What the exception covers

- Every exam on the platform as it is today. Nothing in the code changes.

## What it does not cover

- **A leak by someone with a staff role.** No technical control stops an author, a reviewer or an exam administrator from sharing a paper before the start. Naming who holds those roles for a high-stakes exam, and reducing it, is a process control that the platform does not provide.
- **A fault in the access check.** If the start-time check were lost, a candidate could draw the paper early. The unit test above and the authorization suite (`M3AuthorizationTests`) are the guard, and they run in CI.

## Revisit when

- Counsel or a security reviewer asks for release at the start time. The usual shape is a key that is issued only when the window opens, so that the stored paper cannot be read before then. It is a large change, because the paper is decrypted by the same key ring as every other question.
- The penetration test reports a way to read the paper early.
- An exam is run for which secrecy of the paper before the start is the main concern, such as a high-stakes mock.
- The preview route's permission is widened to more roles.
