# NFR-7 accessibility audit (WCAG 2.1 AA)

**Requirement:** NFR-7, accessibility: WCAG 2.1 AA, keyboard navigation, screen-reader support, per-candidate extra time (see `exam-platform-requirements.md`, NFR-7).
**Issue:** [#87, NFR-7 accessibility audit (WCAG 2.1 AA)](https://github.com/sagarworlds/Online-Exam-Platform/issues/87).
**Date of audit:** 2026-10-12. **Audited base:** `e00e3a8` (the `origin/main` head when the audit started). **Line references:** checked against `origin/main` at `ad7f321`.
**Branch:** `SP/audit/accessibility-wcag`.
**Status:** the audit recorded the findings below on 2026-10-12. Section 6 gives each finding's status on `main`, and the coverage of a later scan of the pages it did not reach.

**Result.** The 17 audited page states do not meet WCAG 2.1 AA. There are 14 findings: 2 High, 9 Medium and 3 Low. Nine of them are Level A failures or Level A decisions, which AA conformance also requires. Automated checks (axe-core) found one violation. The manual checks found the rest, and most concern keyboard focus, which axe cannot judge.

---

## 1. Scope and method

### 1.1 Build and serving

- `apps/web`: `npm ci`, then `npm run build`, the same commands as the `lint-build-test-web` job in `.github/workflows/ci.yml`. The build passed (exit 0).
- The Angular CLI needs Node 22.22.3 or later. The sandbox default was Node 22.22.0, so the build used Node 22.23.3, the latest 22.x release, which is what CI's `node-version: "22"` resolves to. The binary was checked against the published SHA-256 sums.
- Lint and unit tests were not run. The audit changes no source, and the CI lint and test steps are outside its scope.
- The built app (`dist/web/browser`) was served locally with an SPA fallback. The API was not running. Every `/api` call was answered by a Playwright route mock. The mock JSON follows the C# records in `apps/api/src/Modules/*/Application/Dtos` and `*/Contracts` (for example `AttemptDto.cs`, `AttemptPaperDto.cs`, `MyExamDto.cs`, `AttemptResultDto.cs`, `AttemptReviewDto.cs`, `CandidateAnalyticsDto.cs`, `ExamItemAnalysisDto.cs`, `ExamDto.cs`, `SectionDto.cs`, `AuthResult.cs`). No real candidate or exam data was used.
- Signed-in pages were reached by placing an unsigned token in local storage. The web app decodes the token for navigation only (`apps/web/src/app/auth/jwt.ts`), and the mock API decided what each page showed.

### 1.2 Tools

| Tool | Version |
|---|---|
| Node.js | 22.23.3 (build and harness) |
| npm | 10.9.9 |
| Angular core, CLI, build | 22.2.0 |
| TypeScript | 6.0.3 |
| axe-core | 4.14.0 (installed in a scratch directory outside the repository) |
| playwright-core | 1.56.0, matched to the pre-installed Chromium revision 1194 |
| Browser | Chromium 141.0.7390.37, from `/opt/pw-browsers/chromium` (no `playwright install`) |

No dependency was added to the repository. The scan harness lives in the session scratch directory and is not committed.

### 1.3 What was run

- **Automated:** axe-core with the tags `wcag2a`, `wcag2aa`, `wcag21a` and `wcag21aa`, on each of the 17 page states, in the light and the dark colour scheme (34 runs). The app has no manual theme switch. It follows `prefers-color-scheme` through the tokens in `apps/web/src/styles/tokens.css`, so both schemes were run.
- **Manual, scripted in the browser:** the keyboard tab order and focus indicators (40 Tab presses per state), focus after each action that removes or disables a focused control, the timer, announcements and error wiring, reflow at 320 and 640 CSS px, the contrast of focus indicators measured from pixels and computed styles, and reduced motion, emulated at runtime.
- **Reflow and 200 % text:** 200 % zoom on a 1280 px window gives the same layout as a 640 CSS px viewport, so 640 px was used for 200 % (WCAG 1.4.4). 320 px was used for 400 % reflow (WCAG 1.4.10). Browser zoom itself was not driven.

### 1.4 Line references and later commits

The audit ran against `e00e3a8`. `origin/main` has since moved to `ad7f321`. Every TypeScript, CSS and HTML line cited in this report was checked against `origin/main`:

- `apps/web/src/app/candidate/exam-attempt/exam-attempt.ts` changed after the audited base. Its references in this report are the `origin/main` lines. On `origin/main`, the paths the findings describe (leaving a section, Save & Next, the submit confirmation, the submit and result path, and the time-up reload) still have the same focus behaviour. `tick()` gained one call, `countQuestionTime()`, which does not change the expiry branch. The findings were not re-run against `origin/main`.
- `apps/web/src/app/analytics/candidate-analytics/candidate-analytics.html` changed below the lines cited here. The cited lines are unchanged.
- Every other cited file is unchanged between the two commits, so its line numbers are the same as in the audit.

---

## 2. Pages covered

All 17 states were reached and rendered with the mock data. None failed to load.

| # | State | Route | Signed in as | Reached | axe, light and dark |
|---|---|---|---|---|---|
| 1 | Sign-in, password tab | `/login` | none | yes | 0 violations |
| 2 | Sign-in, one-time-code tab | `/login` | none | yes | 0 violations |
| 3 | Register | `/register` | none | yes | 0 violations |
| 4 | Reset password, request | `/password-reset/request` | none | yes | 0 violations |
| 5 | My exams | `/my-exams` | candidate | yes | 0 violations |
| 6 | My exams, "ask for another attempt" form open | `/my-exams` | candidate | yes | 0 violations |
| 7 | Exam instructions | `/my-exams/:id/start` | candidate | yes | 0 violations |
| 8 | Exam page | `/attempt/:id` (in progress, two sections, five questions) | candidate | yes | 0 violations |
| 9 | Exam page, submit confirmation open | `/attempt/:id` | candidate | yes | 0 violations |
| 10 | Exam page, "Display and help" open | `/attempt/:id` | candidate | yes | 1 violation (see A-03) |
| 11 | Result | `/attempt/:id/result` | candidate | yes | 0 violations |
| 12 | Review of answers | `/attempt/:id/review` | candidate | yes | 0 violations |
| 13 | My performance (analytics) | `/analytics` | candidate | yes | 0 violations |
| 14 | Admin home | `/admin` | staff (exam.read, exam.manage) | yes | 0 violations |
| 15 | Exams list | `/exams` | staff | yes | 0 violations |
| 16 | Exam editor, published exam | `/exams/:id` | staff | yes | 0 violations |
| 17 | Item analysis | `/exams/:id/item-analysis` | staff | yes | 0 violations |

Targeted state checks, run by hand and not by axe: the section-lock confirmation on the exam page, the "Delete draft" confirmation on the exam editor (a draft exam), and a rejected sign-in.

Axe "incomplete" items on several states were judged one by one. Section 5 records each one.

### 2.1 Pages not covered, and why

Every item here was outside the page list given for this audit, or was not reachable with the mocks used. None was run, so none is claimed as checked.

- **Sign-in and account flows:** the one-time-code verification step (`/verify-otp`, which the one-time-code sign-in leads to), the password reset confirmation (`/password-reset/confirm`), profile (`/profile`), consent (`/consent`), notifications (`/notifications`) and the forbidden page (`/forbidden`). The request forms themselves were covered.
- **Invitations and batches:** `/invite`, `/invites`, `/invites/create`, `/batches`, `/batches/create`, `/batches/:id/roster`.
- **Guardian portal:** `/guardian`, `/guardian/register`, `/guardian/confirm-link`, `/guardian/link-candidate`.
- **Candidate pages not on the list:** the leaderboard (`/my-exams/:id/leaderboard`).
- **Staff pages not on the list:** the exam builder (`/exams/create`), exam attempts (`/exams/:id/attempts`), scheduler (`/exams/:id/schedule`), risk flags (`/exams/:id/risk-flags`), preview (`/exams/:id/preview`), question bank (`/admin/questions`, `/admin/questions/:id/edit`), books (`/admin/books`, `/admin/books/:id`), attempt requests, disputes and issue reports (`/admin/attempt-requests`, `/admin/disputes`, `/admin/issue-reports`), OTP codes, WhatsApp test, branding and instruction templates.
- **Exam page states not run:** paused, terminated or invalidated attempts, the time-up path (code read only, see A-06 and A-07), low-bandwidth mode, the offline answer sync, focus-violation warnings and the full-screen offer, and the report-issue form.
- **Languages:** only English was run. Hindi and Marathi were not.

---

## 3. Severity scale

- **High:** a Level A failure, or a loss of focus or announcement on the candidate's exam path that leaves a keyboard or screen-reader user unable to continue without starting again from the top of the page, or that hides an irreversible action.
- **Medium:** an AA failure, a Level A focus loss on a secondary path, or a point that needs a decision from the owner.
- **Low:** a semantic or minor issue with a limited effect, or an AA item that has a workaround.

---

## 4. Findings

Each finding gives the page, the WCAG success criterion (SC), how it was found, the severity and the file and line in `apps/web`. "Manual" means a scripted browser check. "axe" means an automated rule. "Code read" means the behaviour was read from the source and not run.

### 4.1 Summary

| ID | Severity | Title | Page | SC | Found by |
|---|---|---|---|---|---|
| A-01 | High | Section-lock confirmation does not take focus | Exam page | 2.4.3 (A), 4.1.2 (A) | Manual |
| A-02 | High | Focus is lost when "Save & Next" reaches the last question | Exam page | 2.4.3 (A) | Manual |
| A-03 | Medium | "A−" and "A+" zoom buttons: visible label not in the accessible name | Exam page, help panel | 2.5.3 (A) | axe `label-content-name-mismatch` |
| A-04 | Medium | Focus ring on answer choices is about 1.6:1 | Exam page | 1.4.11 (AA), 2.4.7 (AA) | Manual (pixel and computed style) |
| A-05 | Medium | Focus ring on text fields is about 1.6:1; the border never changes | Sign-in, register, reset (measured); other `.field` forms (code read) | 1.4.11 (AA), 2.4.7 (AA) | Manual (computed style) |
| A-06 | Medium | Timed exam gives no warning and no time-up announcement | Exam page | 2.2.1 (A), decision needed | Manual and code read |
| A-07 | Medium | Focus is lost after the exam is submitted | Exam page | 2.4.3 (A) | Manual |
| A-08 | Medium | Focus is lost when the "ask for another attempt" form opens or is cancelled | Candidate exam list | 2.4.3 (A) | Manual (open, cancel); code read (send) |
| A-09 | Medium | Focus is lost after a rejected sign-in (also register and reset) | Sign-in | 2.4.3 (A) | Manual (sign-in); code read (register, reset) |
| A-10 | Medium | Focus is lost when "Delete draft" opens its confirmation, and on cancel | Exam editor (draft) | 2.4.3 (A) | Manual |
| A-11 | Medium | Field errors are not announced and focus does not move to them | Sign-in, register, reset | 3.3.1 (A) met in text; 4.1.3 (AA) decision | Manual |
| A-12 | Low | `aria-label` on a generic `span` (review marks) | Review page | 4.1.2 (A) | axe `aria-prohibited-attr` (incomplete) |
| A-13 | Low | Analytics tooltip appears on pointer hover only | My performance | 1.4.13 (AA) | Code read |
| A-14 | Low | Staff menu at phone width scrolls sideways inside its strip | Staff pages at 320 and 640 px | 1.4.10 (AA), decision | Manual (reflow) |

### 4.2 Detail

**A-01. Section-lock confirmation does not take focus (High).**
- Page: exam page, with section locking on (`/attempt/:id`).
- SC: 2.4.3 Focus Order (A). The `alertdialog` is not given focus, so the next keystroke is not in the dialog. 4.1.2 Name, Role, Value (A): a dialog that does not take focus does not reliably announce itself.
- Found by: manual. Enter on "Question 4, not visited" in the second section opened `role="alertdialog"` named "Confirm leaving the section" with the text "Move to Optics? You will not be able to come back to this section." Focus stayed on the palette button outside the dialog (`activeInsideDialog: false`).
- The dialog says the move cannot be undone. Because focus is not on the dialog, a screen-reader user may not hear it before choosing "Yes, move on".
- Where: `apps/web/src/app/candidate/exam-attempt/exam-attempt.html:185-193` (dialog). `apps/web/src/app/candidate/exam-attempt/exam-attempt.ts:521` sets `leavingTo` in `goTo` with no focus call. The submit confirmation, `askToSubmit` and `cancelSubmit` at `apps/web/src/app/candidate/exam-attempt/exam-attempt.ts:786-795`, does move focus, so this is an omission, not a design choice.
- Fixed in: `788a8d7`.

**A-02. Focus is lost when "Save & Next" reaches the last question (High).**
- Page: exam page.
- SC: 2.4.3 Focus Order (A).
- Found by: manual. Focus on "Save & Next" at question 4 of 5, then Enter. The page moved to question 5 of 5. The button is `disabled` on the last question (`isLast()`), so it lost focus, and `document.activeElement` became `BODY`. The next Tab starts again at the top of the page.
- Where: `apps/web/src/app/candidate/exam-attempt/exam-attempt.html:181` ("Next", `[disabled]="isLast()"`) and `:182` ("Save & Next", `[disabled]="isLast()"`). `apps/web/src/app/candidate/exam-attempt/exam-attempt.ts:512-528` (`goTo`) and `:570-574` (`saveAndNext`) move the question without moving focus to a control that remains.
- Fixed in: `c4d60af`.

**A-03. "A−" and "A+" zoom buttons: visible label not in the accessible name (Medium).**
- Page: exam page, "Display and help" panel.
- SC: 2.5.3 Label in Name (A). A user who speaks the visible label ("A plus") cannot activate the control.
- Found by: axe `label-content-name-mismatch` (violation, serious). It appears in both colour schemes. The nodes are `button[aria-label="Larger text"]`, visible text "A+", and `button[aria-label="Smaller text"]`, visible text "A−".
- Where: `apps/web/src/app/candidate/exam-attempt/exam-attempt.html:106` and `:108`.
- Fixed in: `8b7ddb5`.

**A-04. Focus ring on answer choices is about 1.6:1 (Medium).**
- Page: exam page, each answer choice.
- SC: 1.4.11 Non-text Contrast (AA), 2.4.7 Focus Visible (AA).
- Found by: manual. The radio input is hidden with `opacity: 0`. Its only focus cue is `.option-choice:has(input:focus-visible) { box-shadow: var(--focus-ring) }`, a 3 px ring at 30 % alpha. The option's border stays `--control-border`. In light mode the ring colour is rgb(186, 204, 244) against white, a contrast of 1.61:1. In dark mode it is rgb(52, 72, 118) against rgb(22, 28, 45), 1.89:1. Both values were sampled from screenshot pixels.
- Where: `apps/web/src/styles/exam.css:232-239` (border stays `--control-border`), `:249-253` (radio hidden) and `:281-283` (ring only). The ring colour is set in `apps/web/src/styles/tokens.css:38` (light) and `:66` (dark).
- Fixed in: `c7b2e95`.

**A-05. Focus ring on text fields is about 1.6:1, and the border never changes (Medium).**
- Pages: measured on sign-in (email) and register (date field). Reset password and every other `.field` input use the same rule (code read), including forms in the staff area.
- SC: 1.4.11 (AA), 2.4.7 (AA).
- Found by: manual, computed style. On focus, `#login-email` and `#register-dob` keep `border-color: rgb(123, 132, 155)`, the control border. The only change is `box-shadow: rgba(26, 86, 219, 0.3) 0 0 0 3px`, the same 1.61:1 ring as A-04.
- Cause: the base rule `.field input:not([type='radio']):not([type='checkbox'])` (specificity 0,3,1) sets `border: 1px solid var(--control-border)`. The focus rule `.field input:focus` sets `border-color: var(--primary)` but has lower specificity (0,2,1), so the base rule wins and the focus border colour never applies.
- Where: `apps/web/src/styles/components.css:72-79` (base rule, border at `:79`) and `:86-93` (focus rule, `border-color` at `:92`).
- Same pattern, from code reading (not measured): the item-analysis sort buttons remove the outline and use only the same ring (`apps/web/src/app/analytics/item-analysis/item-analysis.css:47-50`).

**A-06. Timed exam gives no warning and no time-up announcement (Medium, decision needed).**
- Page: exam page.
- SC: 2.2.1 Timing Adjustable (A). The exam sets a time limit. Unless the owner records that the limit is essential (an exception under 2.2.1), a warning shortly before expiry and a way to extend the time are needed. Staff can set per-candidate extra time (FR-49, NFR-7), but the candidate has no way to extend the time.
- Found by: manual and code read. The countdown changes every second ("40:00" to "39:58" in 2.1 s). It is `role="timer"` labelled "Time left", and the timer role does not announce changes. No live region announces any threshold. The live regions on the page are the protection notice, the sync message, the zoom percentage and the activity indicator, and none of them carries the time. When the timer reaches zero, `tick()` stops and reloads the attempt, with no alert (code read; the time-up path was not run).
- Where: `apps/web/src/app/candidate/exam-attempt/exam-attempt.html:29-31` (timer) and `apps/web/src/app/candidate/exam-attempt/exam-attempt.ts:1008-1028` (`tick`; the expiry branch is `:1023-1027`, and `:1017` records question time, which does not affect expiry).
- Decision for the owner: either record the essential-time exception for the exam timer, or add an announced warning and an extension path.

**A-07. Focus is lost after the exam is submitted (Medium).**
- Page: exam page, after Submit.
- SC: 2.4.3 Focus Order (A).
- Found by: manual. Submit exam, then the confirmation is shown. Enter on its primary button replaced the attempt with the result card, and `document.activeElement` became `BODY`. No focus was moved to the result heading.
- Where: `apps/web/src/app/candidate/exam-attempt/exam-attempt.ts:832-848` (`sendSubmit`) calls `show()` at `:837`. `show` is defined at `:872` and makes no focus call. The result card is at `apps/web/src/app/candidate/exam-attempt/exam-attempt.html:236-245`. The time-up path (A-06) reuses `show()` through `reload()` (`apps/web/src/app/candidate/exam-attempt/exam-attempt.ts:1030-1038`); that path was read in code, not run.

**A-08. Focus is lost when the "ask for another attempt" form opens or is cancelled (Medium).**
- Page: candidate exam list.
- SC: 2.4.3 Focus Order (A).
- Found by: manual. Enter on "Ask for another attempt" removed that button from the page (`canAskForAttempt` is false while the form is open) and left focus on `BODY`. Cancel also left focus on `BODY`. A successful send is not run; the same code path removes the form, so focus is expected to drop there too (code read).
- Where: `apps/web/src/app/candidate/my-exams/my-exams.html:55-57` (button, hidden while the form is open) and `:70-90` (form). `apps/web/src/app/candidate/my-exams/my-exams.ts:84-92` (`openRequestForm`, `closeRequestForm`) and `:95-113` (`sendRequest`) do not move focus. Compare the report-issue form, which does (`apps/web/src/app/candidate/exam-attempt/report-issue.ts:107`, `:132`).

**A-09. Focus is lost after a rejected sign-in (Medium).**
- Page: sign-in. The same markup is on register and reset password.
- SC: 2.4.3 Focus Order (A). The error message itself is announced, and that part passes (section 6).
- Found by: manual on sign-in. A wrong password produced `role="alert"` with "The e-mail or password is not right.", and `document.activeElement` became `BODY`. The submit button is `[disabled]="submitting()"` during the request, and Chromium drops focus from a control that becomes disabled. Focus is not returned to the button afterwards. The run used a pointer click; the same button handles Enter.
- Code read, same mechanism: `apps/web/src/app/auth/register/register.html:71` and `apps/web/src/app/auth/password-reset-request/password-reset-request.html:30`.
- Where: `apps/web/src/app/auth/login/login.html:71` (password submit) and `:94` (one-time-code submit). `apps/web/src/app/auth/login/login.ts:83-100` (`submitPassword`).

**A-10. Focus is lost when "Delete draft" opens its confirmation, and on cancel (Medium).**
- Page: exam editor, for a draft exam.
- SC: 2.4.3 Focus Order (A), and 4.1.2 (A) for an `alertdialog` that is not given focus.
- Found by: manual. The "Delete draft" button is replaced by `<span role="alertdialog">` holding a "Delete draft" button and a "Cancel" button. Focus was on `BODY` when it opened, and again after Cancel.
- Where: `apps/web/src/app/exam-authoring/exam-editor/exam-editor.html:318-326`.

**A-11. Field errors are not announced, and focus does not move to them (Medium, decision needed).**
- Pages: sign-in (password and one-time-code), register, reset password.
- SC: 3.3.1 Error Identification (A) is met: each error is text, and the field points to it with `aria-describedby` and `aria-invalid`. 4.1.3 Status Messages (AA) is a judgement: the error appears without a live region, and focus stays on the submit button.
- Found by: manual. An empty submit on sign-in set `aria-invalid="true"` and `aria-describedby="login-email-error"`. The errors "Enter your email address." and "Enter your password." have no `role` and no `aria-live`. Focus stayed on "Log in". Register (three fields) and reset password (one field) behaved the same way.
- Where: `apps/web/src/app/auth/login/login.html:40-42`, `:63-65` and `:85-87`. `apps/web/src/app/auth/register/register.html:16`, `:32`, `:64`. `apps/web/src/app/auth/password-reset-request/password-reset-request.html:23`.
- Decision for the owner: move focus to the first invalid field, or add a polite live region for the summary.

**A-12. `aria-label` on a generic `span` (review marks) (Low).**
- Page: review of answers (`/attempt/:id/review`).
- SC: 4.1.2 Name, Role, Value (A). In ARIA 1.2, `aria-label` is prohibited on the generic role, and a `span` has none, so the label may be dropped and the mark ("+2") read without its meaning.
- Found by: axe `aria-prohibited-attr` (reported as incomplete, serious), in both schemes, on `.review-question__marks`.
- Where: `apps/web/src/app/candidate/attempt-review/attempt-review.html:109`. The same pattern is at `apps/web/src/app/attempt-management/exam-attempts/exam-attempts.html:203` (staff page, not audited).

**A-13. Analytics tooltip appears on pointer hover only (Low).**
- Page: my performance (`/analytics`).
- SC: 1.4.13 Content on Hover or Focus (AA). Code read only, not run.
- Where: `apps/web/src/app/analytics/candidate-analytics/candidate-analytics.html:69` (`(mouseenter)="show(i)"`), `:53` (`mouseleave`) and `:87-90` (tooltip). There is no focus handler.
- Mitigation: the results list below the chart (`apps/web/src/app/analytics/candidate-analytics/candidate-analytics.html:101` onward) carries each result's exam name, date and score, so keyboard and screen-reader users have the same figures. The chart itself is `aria-hidden`.

**A-14. Staff menu at phone width scrolls sideways inside its strip (Low, decision needed).**
- Pages: every staff page at 320 and 640 CSS px.
- SC: 1.4.10 Reflow (AA). The page does not scroll sideways, and the menu links stay in the page and are reachable by keyboard. The links beyond the strip's edge need a horizontal scroll inside the strip. Whether that meets 1.4.10 for a navigation component is a judgement.
- Found by: manual. Document `scrollWidth` equals `clientWidth` at 320 and 640 px. Between 50 and 65 elements (links, icons and labels) extend past the right edge of the viewport, inside the menu strip.
- Where: `apps/web/src/app/shared/admin-sidebar/admin-sidebar.css:87-94` (strip, `overflow-x: auto`) and `:111-118` (links do not wrap).

---

## 5. Automated results and how each item was judged

| Result | Where | Judgement |
|---|---|---|
| Violation `label-content-name-mismatch` | Exam page, help panel (both schemes) | A-03 |
| Incomplete `label-content-name-mismatch` | Admin sidebar toggle, six staff states | Pass. The toggle's visible content is an `aria-hidden` glyph ("«" or "»"). No visible text is in the name, so 2.5.3 does not apply. |
| Incomplete `color-contrast` | Item analysis, sort arrow ("▲") | Pass. The arrow is decorative, `aria-hidden`, and not text. |
| Incomplete `color-contrast` | Analytics, trend label "68%" | Pass by measurement. The label colour rgb(23, 32, 51) on the page background rgb(243, 245, 250) is 14.9:1 in light. In dark, rgb(230, 234, 245) on rgb(14, 19, 32) is 15.4:1. |
| Incomplete `aria-prohibited-attr` | Review page, marks | A-12 |

Rules that axe reported as passing on every state in both schemes include `document-title`, `html-has-lang`, `bypass`, `button-name`, `link-name`, `aria-required-attr` and `duplicate-id-aria`, and `color-contrast` had no violation in either scheme. `label` is reported as not applicable on states with no form fields.

---

## 6. What passed

Each check is marked manual (run in the browser) or code read (from the source, not run).

**Automated (axe, WCAG 2.0/2.1 A and AA)**
- 16 of 17 page states have no violation in either colour scheme. The exception is the exam page with the help panel open (A-03).
- `document-title` passes on every state, so each page has a title. For example, the sign-in page is titled "Log in · Online Exam Platform" and the exam page "Exam · Online Exam Platform".
- `html-has-lang` passes on every state (`lang="en"`, `apps/web/src/index.html:2`). The interface language is also set on the document when it changes (`apps/web/src/app/i18n/i18n.service.ts:69`, `:82`, `:94`, code read). Switching the language was not run.

**Manual**
- Skip link (2.4.1): the first Tab shows "Skip to main content", and Enter moves focus to `#main-content` (`apps/web/src/app/app.html:2`, `:45`; `apps/web/src/app/app.css:2-25`, code read for the markup and styles, run for the behaviour).
- Exam page tab order (2.4.3): the first 22 stops are in a logical order. They run through the header links, log out, the language select, Submit, "Report an issue", "Display and help", the answer choices, Mark for review, Report a problem, Next, Save & Next, and then the question palette. Every stop had a visible indicator; the answer choices' indicator is the weak one in A-04. The loss of focus at Save & Next is A-02.
- Tab sweep of the 13 states (2.4.7): 40 Tab presses on each state. Every stop had a visible indicator except two stops on the register date field. A direct check found the 30 % halo present on that field, so the stops are the weakness in A-05 and are not counted separately. Focus returning to the body at the end of the tab cycle is normal and was not counted as a loss.
- Submit confirmation (2.4.3): focus moves into the confirmation sentence when it opens, and returns to "Submit exam" on "Keep working".
- Report-issue form (2.4.3, code read): focus moves into the form when it opens and returns to its button on cancel (`apps/web/src/app/candidate/exam-attempt/report-issue.ts:107`, `:132`). Not run.
- Palette status (1.4.1): each question's state is shown by more than colour. Answered is a solid green border, "seen" is a dashed border, "marked" carries a dot, and the current question has a ring (`apps/web/src/styles/exam.css:326-368`, computed styles checked in the light scheme). The palette buttons carry the state in their accessible names ("Question 2, marked for review", `apps/web/src/app/candidate/exam-attempt/exam-attempt.html:215`).
- Timer (4.1.3): the countdown does not announce each second. The timer role is not a live region, and the protection and sync messages are `aria-live="polite"` (`apps/web/src/app/candidate/exam-attempt/exam-attempt.html:91`, `:93`). Whether a threshold warning is needed is A-06.
- Sign-in server error (4.1.3): "The e-mail or password is not right." appears in `role="alert"` (`apps/web/src/app/auth/login/login.html:68-70`).
- Field identification (3.3.1, text): every field error is text, and the field carries `aria-invalid` and `aria-describedby` (A-11 covers the announcement).
- Reflow at 320 px (1.4.10): no page scrolls sideways, and no element extends beyond the viewport on sign-in, my exams, the exam page, the result and analytics. The exception is the staff menu strip (A-14).
- Text at 200 % (1.4.4, as a 640 px viewport): no page scrolls sideways on the same five pages, and no element extends beyond the viewport.
- Reduced motion (the team's own runtime check; the criterion that covers motion, 2.3.3, is AAA): with `prefers-reduced-motion: reduce`, the API activity indicator's animation does not run, and it does without the setting (`apps/web/src/app/shared/api-activity/api-activity.css:78-93`).
- Dark theme: the same axe results in both schemes (section 5). The option ring was measured in dark (A-04). The palette states were checked in the light scheme only.
- Trend chart (1.1.1, code read): the SVG is `aria-hidden` and the results list below carries each result's details (`apps/web/src/app/analytics/candidate-analytics/candidate-analytics.html:55`, `:101`).
- Item analysis tables (1.3.1): each table has an `aria-label` and `th scope="col"` headers (`apps/web/src/app/analytics/item-analysis/item-analysis.html:57-61`, `:111-119`, code read). At 375 px Chromium still exposes the table, row and cell roles in its accessibility tree (manual). Safari and VoiceOver were not checked.
- Navigation badge (1.1.1, code read): the unread count is `aria-hidden` and the words are visually hidden (`apps/web/src/app/app.html:27-30`).
- Health indicator (1.1.1, code read): each state is `role="img"` with a name (`apps/web/src/app/app.html:54-65`).
- Zoom (1.4.4, code read): the viewport meta does not block zoom (`apps/web/src/index.html:7`).
- Shortcuts (2.1.4, code read): N, P, M and C are single-key shortcuts. They do not act while typing in a text field (`apps/web/src/app/candidate/exam-attempt/exam-shortcuts.ts:22-33`), and there is a switch to turn them off (`apps/web/src/app/candidate/exam-attempt/exam-attempt.html:113`). The switch is in the collapsed "Display and help" panel, so it is easy to miss. This passes 2.1.4 as written.
- Language (3.1.1): `html lang="en"` at load (`apps/web/src/index.html:2`).

---

## 7. Not verified, and limits

- **No screen reader was run.** The announcement findings (A-01, A-06, A-07, A-09, A-11) rest on the focus state, the ARIA roles and the live-region attributes in the DOM. What a screen reader says has not been heard.
- **One browser:** Chromium 141. NFR-8 requires the current two versions of Chrome, Edge, Firefox and Safari. Those were not run.
- **Mock data:** the API responses are fixed fixtures. The paused, terminated, invalidated and time-up states were not run (A-06 and A-07 describe the time-up code path by reading it).
- **200 % zoom** was emulated with a 640 px viewport and not with the browser's zoom control. For reflow the two give the same layout.
- **Keyboard:** Tab order was swept from the top of each page. Arrow-key behaviour in the answer groups was checked only as one tab stop per question.
- **Not evaluated:** WCAG 2.2 criteria, which are outside the 2.1 standard this audit uses: target size (2.5.8) and focus not obscured (2.4.11).
- **Code-read observations, not run:**
  - `apps/web/src/styles/rich-text.css:115` removes the outline of the rich-text editing area (`.rich-editor__content { outline: none }`). The rich-text editor is used on question-editing pages, which are not in this audit, so the focus state of that area was not checked.
  - `apps/web/src/app/exam-authoring/exam-editor/exam-editor.ts:90` reads `this.exam()?.scope.type`, which throws if `scope` is null. The API maps `Scope` on every exam (`apps/api/src/Modules/ExamAuthoring/ExamPlatform.Modules.ExamAuthoring.Application/ExamMapping.cs:40`), so this is not reachable from the current API. It is noted because a first run of this audit hit it with a fixture that omitted `scope`. It is not an accessibility finding.

## 8. Other observations (best practice, not WCAG 2.1 AA)

- The candidate exam list uses `h3` for each exam name under the page's `h1`, with no `h2` (`apps/web/src/app/candidate/my-exams/my-exams.html:20`). Heading order is a best-practice check and is not a WCAG 2.1 failure.
- The staff sidebar toggle changes its name between "Expand admin menu" and "Collapse admin menu" while `aria-expanded` is also set (`apps/web/src/app/shared/admin-sidebar/admin-sidebar.html:5-7`). A stable name with `aria-expanded` is the usual pattern.
- The sidebar width transition (`apps/web/src/app/shared/admin-sidebar/admin-sidebar.css:16`, 0.15 s) is not disabled under reduced motion. The motion criterion that covers this (2.3.3) is AAA, so it is not a finding.

---

## 9. Reproduction

1. In `apps/web`, run `npm ci` and `npm run build` with Node 22.22.3 or later.
2. Serve `apps/web/dist/web/browser` with an SPA fallback to `index.html`.
3. In a scratch directory outside the repository, install `axe-core@4.14.0` and `playwright-core@1.56.0`. Launch Chromium with `executablePath: '/opt/pw-browsers/chromium'`. Register route mocks for `**/api/**`, shaped from the DTO records listed in section 1.1.
4. Run axe with `runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] }` on each state in section 2, in both colour schemes. Then run the scripted checks in section 1.3.

---

## 6. Status on `main`, 2026-10-13

Each finding was checked against the code on `main`, as of this section's date. A-06, A-11, A-13 and A-14 were fixed after the audit, and their rows say how.

| ID | Status | Where |
|---|---|---|
| A-01 | Fixed | `788a8d7` |
| A-02 | Fixed | `c4d60af` |
| A-03 | Fixed | `8b7ddb5` |
| A-04 | Fixed | `c7b2e95` |
| A-05 | Fixed | `eae5289` |
| A-06 | Partly fixed: the owner chose an announced warning. The candidate hears a warning at five minutes and at one minute left, and a time-up notice before the attempt closes. WCAG 2.2.1 also needs either an essential-time exception or a way to extend the time, and neither is in place, so this finding stays open for that part | `exam-attempt.ts`, `exam-attempt.html` |
| A-07 | Fixed | `eae5289` |
| A-08 | Fixed | `eae5289` |
| A-09 | Fixed for sign-in, register and reset | `eae5289`, `9ca98ca` |
| A-10 | Fixed | `eae5289` |
| A-11 | Fixed: when a sign-in, register or reset form is submitted with a missing or wrong field, focus moves to the first such field, and its error is read with it. Focus was chosen over a live region, since a live region announces the error but leaves the candidate at the submit button | `shared/focus-first-invalid-field.ts`, `login.ts`, `register.ts`, `password-reset-request.ts` |
| A-12 | Fixed: the marks label is visually hidden text, so the `aria-label` on the span is gone | `eae5289` |
| A-13 | Fixed on this branch: Escape closes the trend tooltip, as WCAG 1.4.13 asks of hover content. The chart stays decorative, so keyboard users keep the results list. The points are not made focusable, because focusable children of an `aria-hidden` element fail axe | `candidate-analytics.ts` |
| A-14 | Fixed on this branch: at 40rem and below the staff menu is a grid of two or more links to a row, and labels wrap inside their links, so nothing scrolls sideways. Measured on `/admin/questions`: at 320 px, 11 of 13 links were past the right edge and the strip scrolled 1,665 px; now none are, at 320, 360, 480 and 640 px. The desktop column is unchanged | `admin-sidebar.css` |

### Scan of the pages section 2.1 lists

On 2026-10-13, 33 of those pages were scanned in light and dark with axe-core, using the WCAG 2.x A and AA tags. API calls were answered by generic mocks: lists empty, and the branding read returning its object shape. Pages were not given real data.

- **25 pages rendered without errors** and had no axe violations in either colour scheme. Their states are the empty or first-load states, so populated lists, rows and dialogs were not scanned.
- **The 8 pages that did not render are now scanned with page-specific data.** The responses were shaped from the web app's DTOs for /profile, /notifications, the leaderboard, the risk-flag queue, the exam preview, the question edit page, the book page and the WhatsApp status. All 8 render with no console errors and have no axe violations in either colour scheme. The two violations reported before, an unlabelled textarea and an empty summary, came from the failed renders and are gone. The axe 'incomplete' items were judged. The admin sidebar toggle is named by its `aria-label`, with its visible glyph `aria-hidden`, so it passes, as the audit found. The rich-text toolbar buttons are named with their visible letter first (for example `B Bold`), so they pass WCAG 2.5.3.
- **Not covered:** populated states (a notification feed with items, risk flags, book chapters, an enabled WhatsApp set-up), the other languages, and the exam-page states listed in section 2.1 (paused, terminated, time-up, offline sync, the focus-violation warnings and the report-issue form). Every page on the section 2.1 list was in this scan.
