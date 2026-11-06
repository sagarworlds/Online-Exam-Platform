# Runbook: the staging environment

**Decision:** D11 in `docs/owner-decisions.md`, answered 2026-10-15. Paid Render and Neon plans for a staging environment now. The production plans are decided before the pilot.
**What it is for:** the load test ([#84](https://github.com/sagarworlds/Online-Exam-Platform/issues/84), `tests/load/README.md`), the restore drill ([#85](https://github.com/sagarworlds/Online-Exam-Platform/issues/85)), and trying a change before it reaches candidates. The free plans cannot do the first: the API sleeps when idle, so a load test on it measures cold starts and not capacity.
**Who does what:** you create the services and the secrets (the owner's accounts are needed). Engineering runs the tests against the address you give.

Render's and Neon's screens change. Where a step below does not match what you see, the intent is what counts, and `docs/deploy-render.md` has the same steps for production.

## Rules for staging

1. **Synthetic data only.** Never copy production data into staging. Staging sits on the same providers as production, outside India (`docs/compliance/nfr-12-exception.md`), and nobody has agreed to their data being used for tests.
2. **Its own secrets.** Generate a new `Jwt__SigningKey` and a new `DataProtection__KeyEncryptionKey`. Do not reuse production's. A copy of a staging database must never open production content, and the other way round. Keep staging's key-ring key in the password manager too, labelled as staging.
3. **Its own e-mail.** Use a separate Brevo API key and sender, so staging never sends from the production sender. The big pool of load-test accounts is meant to be created by a tool, not through the web form (see "The test accounts"), and its addresses should be ones that cannot receive mail (for example on the reserved `.invalid` domain), so nobody real is e-mailed. The few accounts made by hand through the form need addresses you can read, because registration sends a code to the address.
4. **Production mode.** Run it with `ASPNETCORE_ENVIRONMENT=Production`, so it has the same start-up checks, headers and limits as production. A Development host behaves differently and would measure something else.
5. **Staging only for load tests.** The scripts must never point at production.

## What to create

| Part | Settings |
|---|---|
| Neon project `examplatform-staging` | A paid plan (see "Choosing the plans"). Postgres 16 or newer. The same region as the production project, so the numbers mean something. Connection pooling off: the host must not contain `-pooler` (`docs/deploy-render.md`, step 1) |
| Render web service `exam-platform-api-staging` | Docker, `apps/api/Dockerfile`, context `apps/api`, health check `/v1/health`, branch `main`. A paid instance, not Free |
| Render static site `exam-platform-web-staging` | Optional for the load test. Needed for the header check and for trying the screens. Build as in `render.yaml`, with `API_BASE_URL` set to the staging API's real address |
| GitHub environment `staging` | The secrets `LOAD_TEST_EMAIL_PATTERN` and `LOAD_TEST_PASSWORD`. The workflow `load-test.yml` reads them |

Create the Render services by hand and do not apply `render.yaml` a second time: its service names belong to production.

### API settings

Copy the production list in `docs/deploy-render.md`, step 5, with these differences.

| Key | Staging value |
|---|---|
| `ConnectionStrings__Postgres` | The Npgsql string of the staging Neon project |
| `Jwt__SigningKey` | Generated. A new value |
| `DataProtection__KeyEncryptionKey` | A new value: 32 random bytes in base64, for example the output of `openssl rand -base64 32` |
| `Cors__AllowedOrigins__0`, `Invite__LinkBaseUrl` | The staging site's real address, if there is one |
| `Brevo__ApiKey`, `Brevo__SenderEmail` | The staging Brevo key and a verified staging sender |
| `Identity__OtpDelivery__Provider`, `Mail__Provider`, `ForwardedHeaders__KnownNetworks__0` and `__1` | As in `render.yaml` |
| `Database__MigrateAndSeedOnStartup` | `true` for a single instance. With more than one instance, run `--migrate-and-seed` once as a job instead (`docs/adr/0002-migrations-and-reference-data-seeding.md`) |
| `OpenAiCompatible__ApiKey` | Leave unset, for the same reason as `Gemini__ApiKey` below: staging marks typed answers by the accepted answers alone and a test sitting spends none of the production allowance. Set a separate key only to test AI marking |
| `Gemini__ApiKey` | Leave unset, so staging marks typed answers by the accepted answers alone and a test sitting spends none of the production key's quota. Set a separate key only to test AI marking, and expect attempts with a differently worded typed answer to wait in Grading until the key works |
| `Notifications__RunKey`, the `WhatsApp__*` and `Sms__*` settings | Leave unset. The scheduled-notifications workflow points at production; do not point it at staging |
| `RateLimiting__Global__PermitLimit`, `Identity__RateLimits__PasswordLogin__PermitLimit` | Raised for the load-test window only (action A9 in the owner pack), and put back after. See `tests/load/README.md`, "Staging prerequisites" |

## The first administrator

A fresh deployed database has no staff account, and the development bootstrap administrator is honoured in Development only (ADR 0002, decision 5). Do not copy a local database in for this (as production did, `docs/deploy-render.md`, steps 2 and 7): create the first administrator with the job built for it ([ADR 0005](../adr/0005-first-administrator-job.md)).

1. Check that the API has started once and logged `Migrated and seeded <Module>` for every module (the check list below), and that the staging mail settings are set (`Brevo__ApiKey`, `Brevo__SenderEmail`). The job refuses to run without a mail server, because the e-mail it sends is the only way in.
2. In the staging API's shell (or as a one-off job, on the paid plan), run: `cd /app && dotnet ExamPlatform.Api.dll --create-first-administrator <the address>`. Use an address you can read, in the case you will sign in with.
3. The log says `Created the first administrator` with the address masked. Open the e-mail and follow its link: it opens the staging site's `/password-reset/confirm` page with the code filled in, so choose a password there. If the link does not open, copy the code printed under it and paste it into that page. Then sign in at `/login`: a second code is e-mailed to the same address.

No password is typed, stored or logged at any step. The job works once: it refuses when a Super Admin exists. It is described in the README ("Create the First Administrator of a Deployed Environment").

## The test accounts

`tests/load/README.md` asks for a pool of candidate accounts that share one password, are enrolled in the load-test exam, and need no second factor. Two jobs create it ([ADR 0006](../adr/0006-load-test-candidate-pool.md)); registration sends a code to each address, so the web form cannot. Run them once the staging API has started, the first administrator has published the load-test exam, and you have its id.

1. Choose the pattern and the pool size. The pattern must contain `{n}` once and end in `.invalid`, which cannot exist, so a pool account is never a real person: for example `candidate-{n}@staging-load.invalid`. The pool is the size the run needs (`LOAD_TEST_ACCOUNT_COUNT`: 20,000 at the defaults (D12), 5 for a smoke run). Use the same pattern and the same password for the GitHub `staging` secrets `LOAD_TEST_EMAIL_PATTERN` and `LOAD_TEST_PASSWORD`.
2. For this run only, set `LoadTest__Enabled=true` and `LoadTest__Password=<the shared password>` in the environment the job runs in (the staging API's own, on the paid plan). The password must meet the password policy. **Remove both afterwards.** The jobs refuse to do anything unless `LoadTest__Enabled` is set, and it is not in `render.yaml`.
3. Create the accounts, then enrol them in the exam:

   ```sh
   cd /app
   dotnet ExamPlatform.Api.dll --create-load-test-candidates --count 20000 --email-pattern "candidate-{n}@staging-load.invalid"
   dotnet ExamPlatform.Api.dll --enrol-load-test-candidates --exam <the exam id> --count 20000 --email-pattern "candidate-{n}@staging-load.invalid"
   ```

   On a developer machine with the database alongside, the first took 9 seconds and the second 107 seconds for 20,000. Against the staging database the second will take longer, because it makes two lookups for each account. Each prints its counts (never an address or the password) and can be run again: a second run adds only what is missing.
4. Run the smoke settings in `tests/load/README.md` first. A pool is for one exam: a fresh exam for each run needs `--enrol-load-test-candidates` again with its id (the accounts are reused).

A job exits with code 2 and changes nothing when it is switched off, the pattern or count is wrong, the password is missing or too weak, the roles are not seeded (run `--migrate-and-seed` first), or the exam does not exist.

## Checks before the first run

1. `<staging API address>/v1/health` answers `Healthy`.
2. The log shows `Migrated and seeded <Module>` once per module.
3. A staff member signs in, creates the load-test exam, publishes it, and a test candidate signs in and starts it.
4. The exam's duration is longer than the ramp, the hold and a margin: a short exam makes the end of the run measure refusals (`tests/load/README.md`, prerequisite 1).
5. Run the smoke settings in `tests/load/README.md` first. Sign-in checks the password with PBKDF2 (ASP.NET Core Identity's hasher), which is slow on purpose, and every virtual user signs in at the start. The smoke run shows whether the instance copes before the full run is tried.
6. Read the p95 figures in the summary and check that `dropped_iterations` is zero. A run with dropped iterations did not deliver the load and does not count.

## The restore drill

Run it once on staging before the pilot, and again whenever the database plan changes. It proves the numbers in NFR-10 and that the key-ring key is kept apart from the database.

1. With synthetic data in place, write down a count of rows in a table (for example `identity."Users"`) and the time.
2. Delete or change a few rows on purpose, and write down the time.
3. Restore the database to the time of step 1 using Neon's restore for the plan (branch from a past time, or restore in place). Write down how long it took from deciding to restore until the data was back.
4. Check the count. Start an API instance on the restored database with the staging key and confirm that questions open. This is the test that the content survives a restore.
5. Start a second instance with a wrong key and confirm that it refuses to start, naming the key, and does not make a new one.
6. Record the recovery point (how much data was lost) and the recovery time (how long it took) next to the NFR-10 figures, in the restore runbook that D13 will produce.

## Choosing the plans

- **Neon:** the plan must keep a history window long enough to restore from, and must not scale to zero during a test. The Free plan reportedly keeps 6 hours of history and the paid plans days (search summaries of Neon's pricing; not checked on its own page). Choose on the pricing page, and write the plan and the window in `docs/owner-decisions.md` under D13.
- **Render:** the smallest paid web service does not sleep. Whether it has the CPU for 1,000 answer saves a second is what the load test finds out, so start small, read the results, and size up. Search summaries report the smallest paid plan at about 7 US dollars a month and the next at about 25. Check the pricing page.
- **Brevo:** the free plan allows about 300 e-mails a day (`docs/deploy-render.md`), which is enough for staging.

## After a run

Staging costs money for as long as it exists. After the load test and the drill, suspend the Render services and, on Neon, scale or delete the project as you prefer. Keep the staging key in the password manager until the project is deleted.
