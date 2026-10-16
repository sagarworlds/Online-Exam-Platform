# ADR 0006: The load test's candidate pool is made by two guarded operator commands

- Status: Accepted
- Date: 2026-10-16

## Context

The load test (NFR-1, NFR-2, `tests/load/README.md`) signs in a different candidate for each virtual user: at the defaults, 20,000 accounts that share one password, need no second factor, and are enrolled in the load-test exam (starting an attempt checks the enrolment). Nothing created them. Registration e-mails a code to each address, so the web form cannot make thousands, and a SQL script would bypass the rules the real code keeps and drift as the schema changes.

Constraints:

- Staging is outside the production data rules (`docs/runbooks/staging.md`): synthetic data only, and no real person may be e-mailed or named.
- The commands must be harmless if they are ever run against the wrong environment.
- The load test must exercise the code real candidates use: their sign-in and the attempt-start check.

## Decision

Two operator commands (ADR 0005), one for each module that owns part of the job:

- `--create-load-test-candidates --count N --email-pattern "candidate-{n}@load.invalid"` (Identity) creates the accounts. Each is an active candidate with a date of birth that makes it an adult, the Candidate role, and the shared password, made the way registration makes one (`User.Register`, then the same role and activation).
- `--enrol-load-test-candidates --exam <id> --count N --email-pattern ...` (Invite) enrols them. Each account gets an invite that is created, given a code and accepted through the invite's own methods, so what the rest of the platform sees as enrolment is exactly what a real accept leaves.

Rules both follow:

- **Off unless switched on.** Nothing happens unless `LoadTest:Enabled` is `true`. It is never in `render.yaml`; a staging job sets it for its run. A leftover password does not turn the commands on.
- **The addresses end in `.invalid`**, a top-level domain reserved so that it cannot exist (RFC 2606). Nothing can be delivered to such an address and nobody can own it, so a pool account is never a real person, and a run against the wrong environment would add inert accounts, not impersonate anyone. The pattern is checked in one place, shared by both commands (`LoadTestAccountPattern`), so the accounts one creates are the accounts the other enrols. The pool is at most 50,000 accounts.
- **The shared password comes from the job's environment** (`LoadTest__Password`), never from the command line, and is checked against the platform's password policy. It is hashed once and the hash shared by every account: the accounts share a password by definition, and hashing is slow on purpose, so doing it 20,000 times would dominate the run. The password is never logged or printed. Unlike the first administrator's password (ADR 0005), it protects synthetic accounts only, on staging.
- **Safe to run again.** A second run creates or enrols only what is missing and changes nothing that exists.
- **No per-account events.** Creating and accepting an invite each raise an event, and the audit trail writes an entry of its own for each: for 20,000 accounts, 40,000 writes. Registering raises an event too, which nothing handles today but anything added later would run for every account. So the events are dropped and each run is recorded once, with counts and no address (`Identity.LoadTestCandidatesCreated`, `Invite.LoadTestCandidatesEnrolled`).
- Exit code 2 means the command refused and changed nothing (switched off, a bad count or pattern, no password or a refused one, no tables or roles yet, an unknown exam).

Measured on a developer machine with the database on the same host: creating 20,000 accounts took 9 seconds and enrolling them 107 seconds. Against a remote database the enrolment, which makes two lookups per account, will take longer.

## Alternatives considered

- **A SQL script.** It bypasses the domain rules and breaks quietly when the schema changes, and it is not tested.
- **The web form or the public API.** Registration e-mails a code to each address; 20,000 mailboxes are not available and the rate limits would stop the run.
- **One command doing both.** It would have to reach into two modules' internals. Each command here uses its own module's types and, for enrolment, only Identity's published contract.
- **Hashing the password per account.** Correct but slow; the accounts share a password and a hash.
- **Addresses on a real test domain.** A pool account could then receive mail or be owned by someone. The reserved domain removes the question.

## Consequences

**Positive:** the full 2x run (D12) can be prepared from the command line on staging. The pool is created and enrolled through real code and tested end to end, from the shared password to starting an attempt.

**Negative, and what limits it:**

- A run on the wrong environment creates up to 50,000 inert accounts. The switch is off by default, the domain is reserved, and the audit entry records it. Removing them is a manual database step.
- The shared password sits in the job's environment for the run. It is a staging test value; remove it afterwards.
- The accounts have no consent records. If attempt start ever checks consent to a notice version (data map G3), the pool will need them.

## Follow-ups

- A command that removes a pool, if leaving thousands of inert accounts on a staging database becomes a problem.
- Batch lookups for the enrolment, if its run time on staging is a nuisance.
