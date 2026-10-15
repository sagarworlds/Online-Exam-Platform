# ADR 0004: Sequence numbers on answer saves

- Status: Accepted
- Date: 2026-10-15

## Context

NFR-4 asks for no answer loss: a client buffer, retry with idempotency keys, and durable server writes. Before this change:

- **The client** (`answer-sync.ts`) kept one pending answer per question, persisted it on the device, and sent the answers one at a time, retrying after 2, 4, 8, 16 and then every 30 seconds.
- **The server** stored an answer with its own clock and replaced any earlier answer to the same question (`SaveAnswerHandler`, `Attempt.RecordAnswer`). Repeating the same save was harmless.
- **There was no key** that ordered one write against another.

The client never sends two requests at once, so it cannot reorder its own writes. The gap is a request the client gave up on that is still alive. A save that timed out, or whose answer was lost, can be held in a proxy or a mobile network and delivered after the retry, or after the candidate has changed their mind. Arrival order is then the wrong order, and three things could go wrong: a late save replaces a newer answer, a late clear removes a newer answer, or a late save brings back an answer the candidate cleared.

Owner decision D20 (2026-10-15, `docs/owner-decisions.md`): add a sequence number to answer saves, so the server ignores a write older than the one it holds. Old clients must keep working.

## Decision

- **Every write to a question's answer carries a sequence number.** A save sends it in the body (`sequence`), and a clear in the query (`DELETE …/answers/{questionId}?sequence=7`). It is a whole number from 1 to 2^53 − 1, the range a browser holds exactly. The client gives each new answer to a question the number one above the highest it knows for that question, and gives every repeat of the same write the same number.
- **The server keeps the highest number applied to each question** in its own table, `examRuntime.AttemptAnswerSequences`, one row per (attempt, question). It is kept apart from the answer on purpose: clearing removes the answer, but the number must stay, or a late save from before the clear would bring the answer back.
- **A write is applied only if its number is higher than the one held.** Otherwise nothing is changed and the answer is `409` with the code `answer_superseded` and the number held in `currentSequence`. A number below 1 or above the maximum is `400 invalid_attempt`.
- **A write refused for another reason does not use its number.** The number is claimed after every other check, inside the aggregate (`Attempt.ClaimAnswerSequence`), so it is claimed and the answer changed together or not at all.
- **A client that sends no number still works.** Its write is applied as the newest, as every write was before, and takes the next number up (held + 1), so the order stays the order of arrival.
- **Two writes to the same question at the same moment cannot both be applied.** The sequence row has a unique index on (attempt, question) and an optimistic-concurrency version (`xmin`). The second writer fails and gets `409 concurrency_conflict`.
- **The page numbers from what the server holds.** Each question of the attempt carries `answerSequence`, the number held (null when nothing was ever written). The page reads it on every load, so a reload, a new device or cleared storage never starts again from one.
- **A write the candidate still wants is sent again above the number held.** On `answer_superseded` the queue raises its number above `currentSequence` and sends again, without putting the choice back. That is what keeps a second tab, or a lost counter, from making an answer vanish. After five such immediate retries in a row it waits with the usual delays instead, so two writers cannot loop. A write that a newer choice has replaced is dropped. A late copy that nobody is waiting for simply ends in the 409.
- **`concurrency_conflict` is no longer a refusal on the client.** The answer is kept and sent again after a delay. Both refusals and lost races used to put the choice back.
- **An error can carry facts for its caller.** `DomainException.Extensions` adds fields beside the code in the problem response, and `StaleAnswerError` uses it for `currentSequence`.
- **The load test sends the numbers** (`tests/load/answer-save.js`), so it measures the save the platform really does.

## Alternatives considered

- **Accept replace-by-arrival (D20 option A).** It leaves the late-copy risk, and the owner chose to remove it.
- **A client timestamp as the order.** Clocks are wrong in ways the platform cannot see, and a candidate whose clock stepped back would lose newer answers.
- **One counter for the whole attempt.** Writes to different questions are independent, and a late write for one question must not be refused because of a newer write to another. It would also make every save contend on one row.
- **An `Idempotency-Key` header with stored responses.** It removes duplicates but does not order two different writes, and it needs somewhere to keep the responses.
- **The number on the answer row.** A clear removes the row, and keeping a "cleared" row would change what a stored answer means for scoring, review and analytics, which all read the answers.
- **Compare-and-swap with an ETag.** It makes the client read before every write, which costs a round trip on the networks where saves already struggle.

## Consequences

- A late copy of an old request can no longer replace a newer answer, remove one, or bring back a cleared one. Tests cover each, and mutating the code (removing the check, or the row version) makes them fail.
- Each save now loads one more collection and writes one more row (the number). The load test (#84) is the place to see what that costs at 2x peak.
- A repeat of a write that was applied, because its response was lost, is refused with 409, and the client sends it again one number higher. The answer is then saved a second time, and its saved-at time moves to the repeat. That costs one extra request and changes nothing else.
- Two tabs of one attempt each number from what they last read. A tab that is behind is told the number held and sends above it, so the later deliberate action wins, as before.
- A client that sends a very large number (up to 2^53 − 1) shuts out smaller numbers for that question in that attempt. The cap keeps every number exact in a browser, and any client recovers by sending above the number it is told.
- Old clients and the new one can be used together on the same attempt without losing an answer.
- The response of a save is still `204`, and `409` was already a status of both routes, so no documented status changes.

## Follow-ups

- Marking a question for review and the time spent on a question are already safe to repeat or reorder (a set, and a running total that only rises), and are not numbered.
- If a measured save needs to be cheaper, the attempt does not have to load every collection for a save. That change is separate from this one.
