# Performance

Covers NFR-1: a question loads in under 500 ms and an answer saves in under 300 ms, at the 95th percentile. The load test that proves it on a real environment is `tests/load` and waits for staging (see [`tests/load/README.md`](../tests/load/README.md)). This page is about what the API does to keep those two calls cheap, and how to see whether a change made them dearer.

## Where the time goes

In production the database is on another machine (Neon, from a Render service), so a request costs about **one network round trip per SQL command**. The number of commands a request runs matters more than anything else it does, and unlike a time it does not depend on the machine measuring it. `AnswerPathStatementBudgetTests` holds the two commonest calls to a count.

| Call | Commands before | Commands now |
|---|---|---|
| Save or clear an answer | 19, plus BEGIN and COMMIT | 6, plus BEGIN and COMMIT |
| Read the attempt | 17 | 12 |

Measured on a developer machine with a 60-question exam, in three alternating pairs of runs with the changes switched off and on (so the machine's speed at the time cancels out):

| | Off | On |
|---|---|---|
| Read, one candidate, p50 / p95 | 96-108 / 126-152 ms | 56-63 / 75-80 ms |
| Save, one candidate, p50 / p95 | 84-90 / 111-130 ms | 39-47 / 53-123 ms |
| 16 candidates at once, read p95 | 473-557 ms | 259-329 ms |
| 16 candidates at once, save p95 | 458-522 ms | 235-307 ms |
| 16 candidates at once, requests per second | 43-45 | 78-92 |

These are microbenchmarks in a Debug build with the server in the test process and the database in Docker on the same machine, so there is no network between them. They are good for comparing a change with the one before, not a prediction for production, where each of the commands removed is a network round trip. The same machine gave different absolute numbers on different days, which is why the comparison is run in one window.

## What is kept in memory, and why it is safe

**A stored question version** (`QuestionBank:VersionCache`). A question's text, options and answer key are stored encrypted, and every save and every page read used to load and decrypt the whole question to learn which option ids it has. A stored version is never edited once it exists (FR-7: `Question` appends one on create, revise and key correction, and nothing edits one), so what is read for a question and a version number is right for good and needs no invalidation. `QuestionVersionInvariantTests` pins the rule the cache relies on: after every operation that changes content, the newest version holds exactly the question's content.

- It keeps the *content* of a version. Where a question is filed, whether it can be used (review status) and whether it still exists are read every time, from plain columns without decrypting anything, so a retired, moved or deleted question is never served from memory. (Deleting a question deletes its versions with it, and only when nothing uses it.)
- The current version is read from its stored version row, not from the question itself, so a question edited while it is being read cannot leave one version's number on another version's text.
- A question with no stored version (from before versions were kept), or a version number that was never stored, is read as the question is now and is not cached.
- What the cache hands out is read-only (the option list and the accepted answers cannot be edited through the interface they are given as), so one reader cannot change the content for the next.
- `MaxBytes` (default 64 MB, as the cache counts it: two bytes a character plus a flat allowance per option; `0` turns it off), `SlidingMinutes` (default 30: an idle version is dropped) and `AbsoluteMinutes` (default 360: no version stays longer than this however often it is read). When it is full a version that does not fit is simply read again next time.
- Decrypted content stays in the process while it is cached. The API already holds each paper decrypted for the length of every request; this keeps it for up to `SlidingMinutes` after it was last read, and never past `AbsoluteMinutes`.
- **After restoring the database to an earlier point, restart the API hosts.** The cache assumes a stored version never changes; a restore followed by new edits can put a different version under a number a host has already kept. `AbsoluteMinutes` bounds how long that could last, and a restart ends it.

**An exam's snapshot** (`ExamAuthoring:SnapshotCache`). Every call a candidate makes rebuilt the exam from four queries. The snapshot is an immutable record, so it is kept for `LifetimeSeconds` (default 15; `0` turns it off, `MaxEntries` default 500). Every change to an exam goes through `ExamAuthoringUnitOfWork.SaveChangesAsync`, which drops all snapshots when it finishes, so on the host that made the change the next read sees it at once. A read that was already running when the change landed cannot put its older result back (each entry carries a generation, checked when it is stored and when it is read).

**With more than one API host, another host sees a change when its entry expires, which is at most `LifetimeSeconds` later.** What the snapshot carries and a candidate's calls act on includes the focus-violation limit (an attempt can be ended at the old limit), the result-release setting (answers can stay visible, or hidden, for that long), the publish state and window, section lock, marking scheme and shuffle flags. On one host none of that applies. On a deployment with several hosts, set `ExamAuthoring:SnapshotCache:LifetimeSeconds` to `0` if a change must take effect at once everywhere, and accept the four extra queries per call.

**What the answer path loads.** A save or clear used to load the whole attempt, which is ten queries (the attempt and nine collections). It now loads the attempt, the one question's saved answer and sequence number, and the paper: two queries. An attempt whose time has run out is read in full before it is closed, because closing scores every answer (`AttemptAccess.LoadOwnedForAnswerAsync`).

## Checking a change

- Run `AnswerPathStatementBudgetTests`. The budgets are what the calls take today, with no slack, so losing any one of the caches or the slim answer load fails a test; a failure prints the commands that ran, so the one that crept in can be seen. Each call is measured three times and the fewest counts, so a command from a background timer landing inside one measurement does not fail it.
- To time a change against `main`, run the same benchmark on both. The integration tests start their own Postgres through Testcontainers; where Windows Application Control stops freshly built test DLLs from loading, build as usual and run them in a Linux container (see the notes in `scripts/verify.sh` for the Docker setup).
- On a real environment, use `tests/load`.

## Not done yet

The heartbeat calls (status every 10 s and a time report about as often) are about four times the save rate and are not in the load test; each loads the whole attempt. The session lookup before every request is one more round trip; caching it would let a revoked session through for the length of the cache, which FR-4 does not allow, so it is a decision for the owner. The attempt's paper and the question filing facts are still two separate reads on a save.
