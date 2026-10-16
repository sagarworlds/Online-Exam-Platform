# ADR 0005: A one-off job gives a deployed environment its first administrator

- Status: Accepted
- Date: 2026-10-16

## Context

Staff sign in with a password and a second factor (FR-3), and no endpoint creates a staff account, so a database with no staff has no way into the admin features. ADR 0002 (decision 5) solves this for a developer's empty database with `Identity:Bootstrap:AdminEmail` and `AdminPassword`, honoured in Development only. It rejected "create the bootstrap administrator in every environment": an account made from configuration would be a standing credential in a deployment file.

That left deployed environments with one way in: copy a local database that already holds an administrator into the new one (`docs/deploy-render.md`, steps 2 and 7). Production was set up that way. A new staging environment (D11, `docs/runbooks/staging.md`) cannot sensibly do the same, and neither can any later environment. The gap was found while preparing staging.

Constraints:

- No password may sit in a deployment file, a job definition, a shell history or a log. Logs are kept for at least 180 days and may go to a separate log store (D14).
- The existing rule that a password reset is refused for an account without a password (FR-1: a code-only candidate must not gain a password login that way) must stay as it is.
- A deployed host has the mail settings already, because every sign-in code is e-mailed.

## Decision

Add one operator command, run as a deployment job like `--migrate-and-seed` (ADR 0002):

```
dotnet ExamPlatform.Api.dll --create-first-administrator <e-mail address>
```

- **The address is the only input.** There is no password option, setting or prompt.
- **The account gets a password nobody sees.** The job creates an active Super Admin whose password is 48 random bytes. It is hashed and never shown, stored or logged, and the first password reset replaces it.
- **The person sets their own password through the ordinary reset link.** The job sends the same e-mail that "Forgot password" sends, by calling the same handler. Because the account has a hash, the existing flow accepts it unchanged. Whoever controls the mailbox is the only one who can sign in, and sign-in then needs the second-factor code sent to the same address.
- **It works only while no Super Admin exists.** A later run refuses (`first_administrator_exists`), so the job can never be used to add an administrator to a running environment. Adding more staff is done by an administrator in the app.
- **It never changes an account that exists.** An address already in use is refused (`duplicate_account`) and the account is not promoted.
- **It checks that mail can be sent before creating anything**, when the host delivers codes by e-mail. An account made while no mail can go out would have a password nobody knows.
- **It is recorded.** One audit entry, `Identity.FirstAdministratorProvisioned`, names the account by id. It has no actor (nobody was signed in) and no address (NFR-6).
- **It never prints the address.** The log carries the masked form (`j***@example.org`), as the sign-in code log does.
- **Exit codes:** 0 when the administrator was created, 2 when the job refused and changed nothing (no address, an address that is not one plain address, no sign-in code delivery set up or no mail server, no tables yet, no roles yet, a Super Admin already exists, or the address is taken). An unexpected failure is thrown, so the process fails loudly.

The seam is `IOperatorCommand` (SharedKernel.Application). A module registers each command and its flag in its own installer (`AddOperatorCommand`). The Host picks the registered flag that is among its arguments, builds only that command, runs it, and exits before the web host starts. The flag is kept apart from the command so that nothing else is built: building every command to read its flag once crashed a job that lacked a setting another command needs. The Host knows no module's commands (ADR 0001, Open/Closed). The load-test accounts are the second use (ADR 0006).

The development bootstrap of ADR 0002 is unchanged and still Development-only.

## Alternatives considered

- **Honour `Identity:Bootstrap` in every environment.** Rejected in ADR 0002: a standing credential in deployment configuration.
- **A job that takes the password** (an argument or an environment variable). The password then sits in the job's definition, the shell history or the process list, and in the dashboard until someone removes it.
- **A job that generates a password and prints it once.** The secret then lives in the job's log, which is retained and may be forwarded to a log store for 180 days or more.
- **Let the password reset accept accounts without a password.** It would also give a code-only candidate a password login by reset, which FR-1 forbids, so the guard would need an exception that knows about administrators. More change, in the most security-sensitive flow.
- **An HTTP setup endpoint that works until the first administrator exists.** Anyone who finds a new environment first could take it over, and it adds an unauthenticated route that matters only once.
- **Copy a local database in.** What production did. It mixes a developer's data and its administrator into a new environment, and leaves a password that someone typed on a development machine.

## Consequences

**Positive:** a new environment gets its first administrator with one command and no secret anywhere. Staging (A14) no longer needs a local database copied in. The job is idempotent in effect: a second run changes nothing and says why.

**Negative, and what limits it:**

- **The address is the proof of identity.** An address mistyped as a real mailbox owned by someone else would give that person the administrator account, and the job would then refuse to run again. The job prints the masked address so the operator can check it, and the runbook says to check it before waiting for the e-mail. Recovery is a manual fix in the database.
- **It depends on mail.** If the e-mail does not arrive, the account exists. Once mail is fixed, "Forgot password" on the sign-in page sends a new link.
- **One process at a time**, as for `--migrate-and-seed`: two concurrent runs for different addresses could both pass the "no Super Admin" check.
- The reset e-mail says its code expires in 10 minutes (the wording is shared with sign-in codes), while a reset link is valid for 30. That is existing wording and is not changed here.

## Follow-ups

- Recovering an environment whose only administrator has lost their mailbox is not covered. A second command (for example one that changes the address of an existing Super Admin) would be the place, if it is ever needed.
- The load-test accounts for staging are the second operator commands on the same seam: [ADR 0006](./0006-load-test-candidate-pool.md).
