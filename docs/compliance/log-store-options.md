# Log store options (NFR-13)

**Decision:** D14 in `docs/owner-decisions.md`, answered 2026-10-15. Use an India-hosted log store that keeps logs for at least 180 days, and set `LogRetention:Days` to match. Client IP addresses stay out of the request logs.
**Issue:** [#89, NFR-9 and NFR-13 observability and CERT-In logging](https://github.com/sagarworlds/Online-Exam-Platform/issues/89).
**Status:** options and what to confirm. **Nothing here was checked on a provider's own pages.** The providers' sites cannot be opened from the environment engineering works in (the lookups fail), so every fact below comes from search summaries read on 2026-10-15 and is marked *reported*. Prices change. Confirm each point with the provider, in writing, before choosing.

## The requirement

- NFR-13 asks for CERT-In-aligned logs with 180-day retention, and section 7.3 of the requirements asks for them in India (`docs/observability.md`).
- Today the application writes JSON logs to its output and keeps none. Startup refuses a declared retention below 180 days, but the setting only records what the store does. Nothing keeps logs for 180 days yet.
- **Counsel question 15** asks whether the 180-day rule applies to the application's logs, and whether they must stay in India. *Reported:* articles on the CERT-In directions disagree on whether logs must be **stored** in India or only be **accessible** from India. Until counsel answers, read it strictly and choose a store whose copies are in India.

## How logs reach a store

*Reported:* Render can stream a service's logs to an external destination, over syslog with TLS, and in its current documentation also over HTTPS, with a token for some providers. Older documentation describes syslog only, so check the live page: [Render, streaming service logs](https://render.com/docs/log-streams). The store must therefore accept one of those, or a small forwarder must sit in between.

## The routes

| Route | Reported facts | Open points |
|---|---|---|
| **1. A cloud provider's log service, in its India region.** For example Amazon CloudWatch Logs in Mumbai, Google Cloud Logging in `asia-south1`, or Azure Monitor in Central India | CloudWatch keeps logs for a period set on each log group. A third-party blog gave about 0.67 US dollars per GB ingested in Mumbai (August 2026), against 0.50 in US East on AWS's own page, and about 0.03 per GB-month of storage with no region named. No price was found for the other two | Needs a cloud account. Render's stream cannot write into CloudWatch directly, so a small forwarder is needed, and none is designed. The cost at the platform's real log volume is unknown |
| **2a. Sumo Logic** | Had a Mumbai deployment (a 2021 press release). A competitor's article says ingestion there stopped on 30 April 2025 and access ends on 30 April 2026 | Written by a competitor and unverified. Treat Sumo Logic as unavailable until it says otherwise in writing |
| **2b. Middleware** (an Indian company) | About 0.30 US dollars per GB, with 30 days of retention by default. Its own material mentions retention of up to 180 days or 6 months, and a customer-hosted option for enterprise plans | **No evidence of India data residency was found.** Ask where the hosted data sits |
| **2c. Grafana Cloud** | Free tier: 14 days of log retention. Paid: 30 days. No India region found | Does not reach 180 days as a hosted service |
| **3. A log server you run in an India region** (for example Grafana Loki or OpenSearch on a virtual machine) | Retention and location are yours | The server, its storage, backups, access control and patching are yours to run. Only worth it if someone will maintain it |
| **4. A store outside India, under the NFR-12 exception** (D14 option B, not chosen) | Render names Datadog, Better Stack and Papertrail as examples of destinations | Only if counsel accepts it. Not chosen on 2026-10-15 |

Route 1 and route 3 are the two this file can describe as certainly in India, because the place is the one you choose. For route 2, residency is what you have to get in writing.

## What to confirm with any provider before choosing

- [ ] Where the primary and any replica copies of the logs physically sit, and whether support access or backups leave India.
- [ ] That retention can be set to at least 180 days for the kind of log used, and what that costs at the platform's volume.
- [ ] How Render's stream is received (TLS syslog or HTTPS, and whether a token is needed), and the limits on rate and line length.
- [ ] That a data-processing agreement is available. The platform masks contact details in logs, but ids and some free text can still be personal data.
- [ ] Who can read the logs, whether sign-in needs a second factor, whether logs can be exported, and whether a log can be deleted on request.
- [ ] The price at the volume you expect. **The volume has not been measured.** Staging (`docs/runbooks/staging.md`) is the place to measure it: run the smoke settings of the load test and read how much the API writes per thousand requests.

## Next step

Counsel answers question 15. Then pick a route (action A15), set up the stream, set `LogRetention:Days` to the store's retention, and tick the open points in `docs/observability.md`. Until a store is chosen, logs are kept only as long as Render keeps its own, whose retention is not verified (`docs/compliance/dpdp-data-map.md`, section 4).
