# NFR-5 security checks

**Requirement:** NFR-5, security: TLS everywhere, encryption at rest, OWASP Top 10, rate limiting, WAF, and question paper encrypted until exam start (see `exam-platform-requirements.md`, NFR-5 and section 15, M8).
**Issue:** [#86, NFR-5 security test](https://github.com/sagarworlds/Online-Exam-Platform/issues/86).
**Status on 2026-10-10:** the automated checks below are in place in CI. The WAF is an open decision for the owner (`waf-decision.md`). The integration tests added here were compiled but not run in this environment, and the header check has not yet been run against the deployed hosts.

## What runs in CI

| Check | Where | Fails the build when |
|---|---|---|
| .NET security analyzers | `apps/api/.editorconfig`, applied by the Build step in `.github/workflows/ci.yml` | any rule in the Security category fires |
| NuGet vulnerability scan | `scripts/security/check-nuget-vulnerabilities.sh`, step in the API job | any known-vulnerable package, at any severity, in any project (transitive packages included) |
| npm audit | `npm audit --audit-level=high`, step in the web job | a high or critical advisory in `apps/web` |
| Access tests (OWASP A01) | `M3AuthorizationTests`, section "one candidate reading another candidate's attempt" | a candidate can read another candidate's attempt, its status, review, result, certificate or analytics |
| API security headers | `SecurityHeadersTests`, `ProductionHostTests` | a response loses a header, or HSTS is not one year |
| Deployed headers | `scripts/security/check-security-headers.sh` through `.github/workflows/security-headers.yml` (weekly and on demand) | the static site or the API serves a header that fails the policy, or a repository variable is not set |

### Analyzers

The analyzers run on every build. The Security category is an error, and the rules are named one by one in `apps/api/.editorconfig` (94 rules, from the .NET 10 SDK 10.0.112). Two details of the configuration matter:

- The SDK's "all rules" configuration is a global config, and a global config outranks `.editorconfig`. A category-level setting alone therefore cannot turn on the security rules that are off by default, such as CA5358 (unsafe cipher modes) and CA5394 (insecure randomness). Each rule is listed for that reason. The category line stays as the catch-all for a rule a later SDK adds that is on by default.
- The list was checked against the SDK's own rule metadata. A future SDK upgrade must re-check it.

Findings the analyzers raised, and how each was resolved:

| Rule | Where | Resolution |
|---|---|---|
| CA5394, insecure randomness | `QuestionPicker.cs`, `RandomQuestionPicker` | Fixed in code. The picker draws each candidate's paper when the attempt starts, so it now uses `RandomNumberGenerator`. |
| CA2100, SQL injection review | `QuestionContentBackfill.ScanColumnAsync` | Suppressed, with a justification. Table and column names come only from the constant `ContentColumns` list; values are parameters. |
| CA2100 | `NonDevelopmentStartupTests.CreateDatabaseAsync` | Suppressed, with a justification. The name is a fixed prefix and a GUID in `N` form. |
| CA5394 | `IdentitySecurityFlowTests.UniquePhoneNumber`, `WhatsAppTestFlowTests.UniqueNumber` | Suppressed, with a justification. Test data that only has to be unique per call. |

The three suppressions are method-level `SuppressMessage` attributes with a written justification. No rule was lowered or removed.

### Dependencies

- **NuGet.** `dotnet list package --vulnerable` exits 0 even when it finds a vulnerable package (checked with SDK 10.0.112). The gate therefore reads the JSON report, and it also requires the text report's project lines. If either is in an unexpected shape, the gate fails. On 2026-10-10 no project in the solution had a known-vulnerable package (72 projects). A throwaway project with `Newtonsoft.Json` 9.0.1 made the gate fail, as intended.
- **npm.** `npm audit --audit-level=high` found one high advisory, in `source-map-js` 1.2.1 (a development dependency of the build tooling). A non-breaking fix updated it to 1.2.2 in `apps/web/package-lock.json`. One low advisory remains, in `katex` (prototype pollution, fixed only by katex 0.19.0, which is a breaking change). It does not fail the high gate. Upgrading katex is a product decision and is not made here.

## Security headers

### What the API sends

`SecurityHeadersMiddleware` sends `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` and `Referrer-Policy: no-referrer` on every response, errors included. In production only, `UseHsts` sends `Strict-Transport-Security`. Its max-age is now one year (the framework default was 30 days), and subdomains are not included. A subdomain policy is a decision about the whole domain and is left to the owner.

### What the static site needs

The static site is served by Render, and the repository sets no headers for it. Render documents custom response headers for static sites, set in the Dashboard (per Render's "HTTP Headers for Static Sites" page). The `headers` field in `render.yaml` is also documented, but its exact syntax was not confirmed, so this change does not put headers in `render.yaml`. The owner sets these in the Dashboard, or adds them to `render.yaml` after checking the syntax against Render's Blueprint reference.

Recommended policy for the static site, built from the current `index.html`. The build contains one inline script (Beasties' critical-CSS loader, which switches the stylesheet from `media="print"` to screen) and one inline `<style>` block:

```
default-src 'self';
script-src 'self' 'sha256-LMY6wYoFV9I4wWzxaq1N/dTpl4iurQktw706UCHK3vM=';
style-src 'self' 'unsafe-inline';
img-src 'self' data: blob:;
font-src 'self';
connect-src 'self' <API origin>;
worker-src 'self';
manifest-src 'self';
object-src 'none';
base-uri 'self';
form-action 'self';
frame-ancestors 'none'
```

- `<API origin>` is a placeholder. The owner writes the API's `https://` origin in when the policy is set. It is not written into the repository.
- The script hash is for the build of 2026-10-10. Re-compute it when the Angular build changes the script. The alternative is to turn off inline critical CSS in `angular.json`, which removes both the script and the need for its hash, at a cost in first-paint performance.
- `style-src 'unsafe-inline'` is needed because Angular adds component styles at runtime and the critical CSS is inline. Removing it needs a nonce, which is a follow-up.
- `X-Frame-Options: DENY` and `Strict-Transport-Security: max-age=31536000` go with the policy in the Dashboard.

### What the header check verifies

`check-security-headers.sh <https-url>` reads the last response of the URL (after any redirect) and requires:

- a Content-Security-Policy with `default-src` and `frame-ancestors`, and no `unsafe-eval`;
- `X-Content-Type-Options: nosniff`;
- frame protection: `X-Frame-Options` DENY or SAMEORIGIN, or a CSP `frame-ancestors` that is not `*`;
- a strict `Referrer-Policy`;
- `Strict-Transport-Security` with a max-age of at least one year.

It refuses plain HTTP, because browsers ignore HSTS over HTTP. It fails closed when the URL cannot be fetched. It was tested against a local self-signed TLS server that sends every header (pass), one that sends bad values (five failures), a plain HTTP URL (refused), and an unreachable port (fails). It has not been run against the deployed hosts.

The workflow reads the URLs from the repository variables `PUBLIC_WEB_URL` and `PUBLIC_API_URL`. The API URL is the base address; the workflow appends `/v1/health`. A missing variable fails the job, because a check that is not configured must not look like a pass. **Until the owner sets both variables, the weekly job will fail.** That is deliberate.

## Access tests (OWASP A01)

The new section in `M3AuthorizationTests` checks, for two signed-in candidates on the same released exam, that the second candidate gets 404 with the `attempt_not_found` title on:

- `GET /v1/me/attempts/{id}` (the attempt),
- `GET /v1/me/attempts/{id}/status`,
- `GET /v1/me/attempts/{id}/review`,
- `GET /v1/me/attempts/{id}/result`,
- `GET /v1/me/attempts/{id}/certificate`,

and that the second candidate's own analytics show no result of the first candidate's. Each case first checks that the owner can reach the same route, so a 404 cannot come from a wrong path. The exam releases results at once, so the refusal comes from ownership and not from a held-back result. These tests were compiled with the solution. They run in CI only, because they need PostgreSQL through Testcontainers.

Several per-resource isolation tests already existed (`AttemptResultFlowTests`, `AnswerReviewFlowTests`, `CertificateFlowTests`, `CandidateAnalyticsFlowTests`, `ExamTakingFlowTests`). The new matrix puts the same rule in the authorization suite, so a new candidate route that is missed is visible there.

## Encryption at rest

Question content is encrypted by `QuestionContentCipher` through the ASP.NET Core Data Protection key ring (#215, with the backfill in `QuestionContentBackfill`). Nothing in this change alters that. The question-paper release rule (encrypted until exam start) is not changed here.

## Not verified here

- The integration tests (`M3AuthorizationTests`, `ProductionHostTests`) were compiled, not run. They need Docker and PostgreSQL in CI.
- The header check has not been run against the deployed static site or API. Render may set headers at its edge that the repository does not show; the check reports what is actually served.
- The Render Blueprint `headers` syntax was not confirmed.
- No penetration test was run. Those are manual, and the requirement's "security test clean" is not covered by these automated checks alone.
