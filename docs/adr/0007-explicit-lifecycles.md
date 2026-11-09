# ADR 0007: Lifecycles are explicit, listed in one place, and checked across the API and the web app

- Status: Accepted
- Date: 2026-11-09

## Context

The platform is full of lifecycles: an exam, a question under review, an invite, a candidate's attempt, a typed answer waiting for the AI
evaluator, and a few dozen screen states on the web. Most are **implicit**: nothing declares "these are the states and these are the
moves". The rules are spread over guards in methods, `if` chains in templates, and clusters of booleans. An audit on 2026-11-09 found:

- **The API is mostly sound.** Each aggregate guards its own moves and refuses a bad one with a named error. The weak spots are two:
  states held in several fields at once (an attempt is `Status` plus a pause time, a termination, an invalidation, a pending revision and
  the clock), and five state names that no code ever reaches.
- **The two sides had drifted, and nothing noticed.** The API names an unopened batch `Pending` and its members `Invited`, `Registered`,
  `Completed` and `Withdrawn`. The web app knew `Draft`, and `Pending`, `Registered`, `Verified` and `Inactive`. Every new batch showed an
  empty status badge, a member who had only been invited did too, and the batch list's "Draft" filter could never match anything. Its
  tests used the web's names, so they passed. The other twelve lists it compares matched; this one did not.
- **The web's screen states are the larger surface.** About a dozen screens encode a lifecycle as flags that can contradict each other
  (for example a page loading and failed at once, or a modal saving and confirming a discard at once). The riskiest is the exam page,
  where a candidate is working. One real overlap was found and fixed: on the code page a new code could be asked for while the current
  one was being checked, so the answer to either arrived to a page that had changed under it.

The constraint is the owner's: nothing that works may break. The exam page is 1,500 lines with a 3,000-line spec, and the answer-saving
path carries NFR-4 (no answer loss).

## Decision

1. **One document lists every lifecycle** (`docs/state-machines.md`): the states, the allowed moves as a diagram, where each is enforced,
   and which states are reserved. It is the place to look first, for people and for agents, and it is updated in the same change as any
   move.
2. **The API's names are the contract, and a test holds the web to it.** `WebStatusVocabularyTests` (architecture tests) reads each
   web union from its source file and compares it with the enum the API serialises, name for name, in both directions. A lifecycle that
   reaches the web gets a row there. The web uses the API's names even where its words differ ("Draft" for `Pending`): the words belong in
   the message tables, the names do not.
3. **A status the page has no words for is shown as it is named**, never as an empty badge (the same fallback the student list already used).
4. **A lifecycle has one status type and its moves live next to it**, refused with a named error. State held in several fields is *read*
   through one function (as `TypedAnswerChecks.StateOf` does for the typed-answer check), not re-derived in a template. A lifecycle that
   needs an audit history follows `Incident`: the table of allowed moves, and an append-only history.
5. **On the web, a screen's own states are one discriminated union in one signal**, as `ConsentState` and `AttentionState` already are. No
   shared state-machine library or helper is added.
6. **Existing implicit lifecycles are changed one at a time, behind their existing specs**, ranked in `docs/state-machines.md`. None is
   rewritten as part of this decision.
7. **Where two operations on one screen must not overlap, each refuses while the other runs**, in the method and on the button. Done now
   for the code page.

## Alternatives considered

- **A state-machine library (XState or similar) on the web, or a generic transition table in the SharedKernel for the API.** It would
  replace guards that work and are tested, and the API's errors differ by lifecycle on purpose (`InviteStateError`, `ExamNotDraftError`,
  `AttemptNotInProgressError`), so a shared table would either flatten them or wrap them. Cost and risk for no change a user can see.
- **Generating the web's types from the API's enums.** It removes the drift outright but adds a build step across two toolchains for fourteen
  small lists. The test gives the same protection with no new step, and can be replaced by generation later without changing the web.
- **Rewriting the exam page's flags as one phase union now.** It is the right end state, but it is the file where a mistake costs a
  candidate their answers, and the gain is not visible. It stays on the ranked list.
- **Deleting the five reserved names.** A status is stored by name or number, so removing one risks a row that can no longer be read, and
  each may be wanted (archive, expiry, suspension). They are listed as reserved instead, and the owner decides.
- **Guarding `User.Activate()` now.** Every current caller already refuses a suspended or deactivated account first, so it changes nothing
  today; it is security-relevant and belongs in its own change with its own tests. Listed in the open questions.

## Consequences

- A status that is renamed or added on one side now fails a test that names both lists, instead of reaching production as a blank badge.
- Someone adding a lifecycle has one document to update and one checklist to follow; an agent has one place to learn the rules instead of
  reading every aggregate.
- The web's batch statuses changed names (`Draft` to `Pending`; `Pending`, `Verified`, `Inactive` to `Invited`, `Completed`, `Withdrawn`),
  the message keys with them. The words shown are the same as before for each state, so no translation changed. A bookmarked
  `?status=Draft` on the batch list is now ignored (it matched nothing before either).
- The test needs the whole repository checked out; it fails with a message saying so, not silently.
- The decision does not make any implicit lifecycle on the web explicit by itself. The ranked list is the work still to do.

## Follow-ups

- Make the exam page's phases and the answer-sync's states explicit (items 1 and 2 on the ranked list), each with its own change, spec-first.
- Move the end-reason precedence for an attempt into one function used by both pages, and decide whether the administrator's list should
  name a violation end (open question 4).
- The four review queues repeat one row machine; extract it when the next one is touched, not before.
- Owner decisions listed under "Open questions" in `docs/state-machines.md`: the reserved states, `Activate()`, and a withdrawn member who registers.
- Revisit generation of the web's status types from the API if the list of rows in `WebStatusVocabularyTests` passes about thirty.
