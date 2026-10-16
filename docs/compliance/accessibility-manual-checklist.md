# Accessibility: the manual pass

**Decision:** D19 in `docs/owner-decisions.md`, answered 2026-10-15. The owner and a few volunteers run this pass. The automated runs on Chromium, Firefox and WebKit are in CI (`apps/web/e2e`, README "Browser Tests"), and the owner checks a few real phones.
**Issues:** [#87, NFR-7 accessibility audit](https://github.com/sagarworlds/Online-Exam-Platform/issues/87) and [#88, NFR-8 browser compatibility](https://github.com/sagarworlds/Online-Exam-Platform/issues/88).
**Why it is needed:** the automated audit (`accessibility-audit.md`) is clean and every finding is fixed, but a program cannot judge what a screen reader says or whether a flow makes sense without a mouse. Section 7 of that audit lists what was never run: no screen reader, one browser (Chromium), mock data, and several pages and exam states.

## Before you start

1. **Environment.** Use staging (`docs/runbooks/staging.md`) or a local run, with synthetic data only. Never use real candidates' accounts.
2. **Accounts.** One candidate, one staff member with the exam permissions, and an exam you can start. For the exam states below, make a short exam (about 8 minutes), so the five-minute and one-minute warnings come quickly.
3. **Who.** The owner, plus two or three volunteers. If someone who uses a screen reader every day can take part, ask them: their way of using it finds problems a beginner's does not. A beginner with NVDA can still catch the big ones.
4. **Tools.**
   - Windows: NVDA (free) with Firefox or Chrome.
   - Mac: VoiceOver with Safari.
   - iPhone: VoiceOver with Safari. Android: TalkBack with Chrome.
5. **Recording.** Fill in the results table at the end. For each failure, write what you did, what you expected, and what happened. A failure becomes an issue labelled `accessibility`, with the journey and the tool in the title.

A **failure** is anything that stops you finishing a task, hides information from you, loses your place (focus drops to the top of the page, or to nowhere), or says something wrong or nothing at all.

## Part 1: keyboard only (no mouse, no touch)

Put the mouse away. Use Tab, Shift+Tab, Enter, Space, the arrow keys and Escape.

- [ ] Every control on each page can be reached and used.
- [ ] You can always see where you are (a clear outline on the focused control).
- [ ] The order of the Tab stops follows the order you read the page.
- [ ] You are never trapped: you can always Tab out, and Escape closes a dialog.
- [ ] The "Skip to main content" link appears on the first Tab and works.
- [ ] After an action that changes the page (Save & Next on the last question, Submit, opening or cancelling a form), your place is sensible and not lost to the top of the page.
- [ ] The single-key shortcuts on the exam page (N, P, M and C) do not fire while you type in an answer box, and there is a switch to turn them off.

## Part 2: a screen reader

Do the same journeys with a screen reader, with your eyes closed or the screen off for the first pass.

- [ ] Each page announces its title, and its headings tell you where you are.
- [ ] Every form field is announced with its label. A wrong or missing field is announced with its error, and focus moves to the first such field (sign-in, register, reset).
- [ ] On the exam page, the countdown does not chatter every second. **A warning is announced at five minutes left and again at one minute left, and a time-up notice before the attempt closes.**
- [ ] The question palette says each question's state in words ("Question 2, marked for review").
- [ ] Saving and syncing messages are announced politely and do not interrupt you.
- [ ] After Submit, the result is announced and your place is the result heading.
- [ ] Maths is read as maths (a fraction, a square root, a power), not as stray symbols. Try questions that have formulas, in English, Hindi and Marathi.
- [ ] Pictures have a useful description, or are ignored when they are decoration.
- [ ] Tables, such as the item analysis, are announced with their headings, and a row makes sense read cell by cell.

## Part 3: seeing and zooming

- [ ] Zoom the browser to 200 percent: no text is cut off and nothing needs sideways scrolling.
- [ ] Narrow the window to 320 pixels wide (or zoom to 400 percent): the page reflows to one column. The staff menu is a grid of links and does not scroll sideways.
- [ ] Turn on the exam page's high contrast and larger text ("Display and help"): everything is still readable.
- [ ] Use the operating system's dark mode and high-contrast mode. Nothing disappears.
- [ ] Turn on "reduce motion" in the operating system. Nothing keeps moving.
- [ ] Nothing depends on colour alone. Each question's state in the palette (answered, seen, marked, current) can be told apart without colour.

## Part 4: real phones

Use an iPhone with Safari and an Android phone with Chrome, on a real network, at least once each. Staff screens at phone width matter less than the candidate's.

- [ ] Sign in, register and reset work with the on-screen keyboard. The keyboard does not hide the field you are typing in.
- [ ] Every button and link can be tapped without hitting its neighbour. The project's bar is 44 pixels.
- [ ] Nothing scrolls sideways, in portrait and in landscape.
- [ ] Take an exam end to end. Turn on aeroplane mode for about a minute in the middle, answer two questions, and turn it back on: the answers are kept and sync, and the page says so.
- [ ] Lock the phone for a minute and unlock it during the exam. The exam is still there and the countdown is right.
- [ ] The full-screen offer can be refused, and the exam goes on.
- [ ] Low-bandwidth mode keeps the exam usable.
- [ ] Rotate the phone during the exam. You do not lose your answer or your place.

## Part 5: languages

Switch the interface to Hindi, then to Marathi, and go through the candidate journeys again.

- [ ] Text fits its box. No label is cut off or overlaps another.
- [ ] Devanagari letters and joined characters display correctly on every device you have.
- [ ] A screen reader reads the page in the right language (it changes voice or says the right words).
- [ ] The language chosen is kept when you move between pages and after a reload.

## Part 6: exam states the audit never ran

Ask a staff member to make each state happen, then go through the candidate's side and the staff side.

- [ ] **The five-minute and one-minute warnings and the time-up notice** (use the short exam).
- [ ] **A paused attempt**, then resumed.
- [ ] **A terminated attempt** and **an invalidated attempt**: the candidate is told why, in words.
- [ ] **Focus warnings**: leave the exam page three times (a warning appears and is announced) and five times (the attempt ends). Check the wording.
- [ ] **Offline answer sync** (see Part 4).
- [ ] **The report-an-issue form**: it opens with focus inside it, closes with focus back on its button, and the message is sent.
- [ ] **Staff pages the audit did not cover:** the question bank and its editor, books, attempt requests, disputes, issue reports, batches and invitations. Keyboard and screen reader, at least once each.

## Results

Copy this table into a new issue, or into a comment on #87, and fill it in.

| Part | Tool and device | Who | Result (pass, partial, fail) | Notes and issue numbers |
|---|---|---|---|---|
| 1 Keyboard | | | | |
| 2 Screen reader, Windows | NVDA | | | |
| 2 Screen reader, Mac | VoiceOver | | | |
| 2 Screen reader, iPhone | VoiceOver | | | |
| 2 Screen reader, Android | TalkBack | | | |
| 3 Seeing and zooming | | | | |
| 4 iPhone Safari | | | | |
| 4 Android Chrome | | | | |
| 5 Hindi | | | | |
| 5 Marathi | | | | |
| 6 Exam states | | | | |

## When it is done

The pass is done when every part has a result, every failure has an issue, and the failures that stop a candidate finishing an exam are fixed and checked again. Then record on #87 the date, who took part, what was tested on, and what was not. The platform has met WCAG 2.1 AA only to the extent that this pass and the automated runs show; neither is a certificate. If a school or regulator wants one, choose option B of D19 (an external auditor).
