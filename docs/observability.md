# Observability and log retention

Covers NFR-9 (observability) and NFR-13 (CERT-In-aligned logging, 180-day retention). The code lives in `apps/api/src/SharedKernel/ExamPlatform.SharedKernel.Infrastructure/Observability` and `.../Health`, with the wiring in `apps/api/src/Host/ExamPlatform.Api/Program.cs`.

## Where the logs go

- The API writes **one JSON object per line to stdout**. It writes no log file.
- The hosting collects stdout. On Render that is the service's log stream (see `docs/deploy-render.md`).
- **The application does not keep or delete logs.** How long they are kept is set in the log store, which is the owner's hosting decision (see "Decisions for the owner" below).

## What a log line holds

| Field | Meaning |
|---|---|
| `timestamp` | UTC, ISO 8601 |
| `level`, `category`, `eventId` | Standard `ILogger` fields |
| `correlationId` | The request's correlation id (below). Absent outside a request |
| `message` | The rendered message, with personal data masked |
| `exception` | The exception and stack trace, with personal data masked |
| `properties` | The message's named values. A value whose name is sensitive is written as `[redacted]` |
| `scope` | Any other log scope open around the line |

Each request writes one outcome line:

`HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs} ms`

- `Route` is the route **template** (for example `/v1/invites/{inviteId}`), never the values filled into it.
- Level: 5xx is Error; 401, 403 and 429 are Warning; other answers are Information. The health probes (`/v1/health*`) are Debug when healthy, so an orchestrator's polling does not drown the log.

## Correlation id

- A caller may send `X-Correlation-Id`. An id of up to 128 letters, digits, `.`, `-` or `_` is kept. Anything else is replaced with a new one, and the request is not rejected.
- The same id appears in the response's `X-Correlation-Id` header, in a failure's problem-details `traceId`, on every log line of the request (`correlationId`), and in the audit trail's `CorrelationId`.
- The CORS policy exposes the header, so the web client can show it to someone reporting a failure.

## What is never logged

- **Request bodies, query strings, headers and path values.** The request line uses the route template, so an invite code in a path, or the WhatsApp webhook's verify token in the query string, is never written. ASP.NET Core also opens a scope with the raw path and query string on every request; the formatter leaves those two scope values out. The framework's own request lines (category `Microsoft.AspNetCore.Hosting.Diagnostics`, which include the query string) are held at Warning whatever the `Logging` configuration says.
- **E-mail addresses, phone numbers, tokens, passwords, OTP codes, answers and question text.** The formatter masks each by its shape (an e-mail address, a phone number, a JWT or bearer token, a labelled `password=` or `OTP:` value) and by the name of its property. Masked contact details the platform writes on purpose, such as `a***@example.com`, pass through unchanged. The tests are in `LogRedactionTests` and `RedactingJsonConsoleFormatterTests`.
- The masking is a last line of defence. Code should not pass these values to a logger in the first place.
- **Known exception:** `LoggingOtpSender` (Identity, development only) still writes the one-time code to the log. Development sign-in depends on it, and startup validation allows that sender only in Development. See "Decisions for the owner".

## Health endpoints (NFR-3)

| Route | Checks | Use |
|---|---|---|
| `/v1/health` | None; answers `Healthy` while the process serves | Render's `healthCheckPath`. Unchanged |
| `/v1/health/live` | None | Liveness: restart the process only when this fails |
| `/v1/health/ready` | The database (`SELECT 1`, 5-second deadline) | Readiness: send traffic only when this answers `200` |

A `503` from `/v1/health/ready` names no reason on the wire, so that a probe reveals nothing about the database. The health-check service logs each check's name, status and message, and the database check's message names the failure without the connection string.

## Log retention (NFR-13)

- Configuration: `LogRetention:Days` in `appsettings.json` (committed as `180`). The owner sets it to the number of days the log store is configured to keep logs.
- **Startup refuses any value below 180.** The minimum is a constant in `LogRetentionOptions`, not configuration, so a deployment file cannot lower it. A unit test also pins the committed value.
- At each start the host logs the declared retention (`Logs are declared to be kept for N days`), so the figure in force can be read from the logs.
- **The setting does not make the store keep anything.** Retention is enforced by the log store. Choosing a store and configuring its retention to at least 180 days is an owner decision, and the setting must match it.
- Nothing in this application purges logs. No purge job exists, and none was added.

## Audit log (FR-40)

- Audit entries are rows in the Postgres table `admin."AuditLogs"`. Each row is append-only: the domain has no method that changes or removes one.
- No code deletes or purges audit rows. A search of `apps/api` found no `ExecuteDelete`, no remove call on an audit entry, and no retention job. The audit log is therefore kept for as long as the database keeps it, which is well over 180 days.
- Keeping it that long is then a matter of the database's backup and retention settings (NFR-10), which the owner controls.
- A retention job (planned for M7) must not purge audit rows before 180 days.

## Metrics

ASP.NET Core already emits `http.server.request.duration` on its own meter (`Microsoft.AspNetCore.Hosting`). No exporter is configured, so the metric is not visible in production yet. Choosing an exporter is a hosting decision; no vendor was added.

## Decisions for the owner

1. **Where logs are stored, and for how long.** Choose a log store that is hosted in India (NFR-12 and NFR-13), configure its retention to at least 180 days, and set `LogRetention:Days` to match. The NFR-12 exception already records that Render-hosted logs are outside India (`docs/compliance/nfr-12-exception.md`), so the store choice has to settle that too. Counsel should confirm the 180-day figure itself.
2. **The development OTP log line.** `LoggingOtpSender` writes the code so that a developer can sign in, including the bootstrap administrator, whose second factor is a code. Removing it means finding another development route first.
3. **Client IP addresses.** The request line does not record them, because an IP address is personal data under the DPDP Act. Whether security logs need them is a decision for the owner with counsel.
4. **Exporting metrics.** Choose an exporter when there is somewhere to send them.
