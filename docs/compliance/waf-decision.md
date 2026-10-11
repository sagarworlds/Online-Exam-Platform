# Decision for the owner: web application firewall (NFR-5)

**Requirement:** NFR-5 lists a WAF next to TLS, encryption at rest, OWASP Top 10 and rate limiting (`exam-platform-requirements.md`, NFR-5).
**Status:** open. This is a hosting decision, so it is the owner's to make. Nothing here creates a vendor account, a credential or a DNS record.
**Date:** 2026-10-10.
**Needed by:** before the M8 security test is signed off.

## Where things stand

- The API and the static site run on Render, and the database on Neon (`render.yaml`, `docs/deploy-render.md`). Render terminates TLS for both. The API has no proxy of its own in front of it, only Render's.
- The API already has application-level controls that a WAF would complement: named rate limits per client, forwarded-header trust limited to Render's proxy range, security headers, and OTP and authorization checks. They do not inspect request bodies for injection patterns, and they do not block known attack signatures. That is the gap a WAF closes.
- I did not find a WAF in the Render documentation I could read. The owner should confirm this with Render before relying on it.
- Tests and analyzers in CI cover code-level injection and vulnerable dependencies. They do not cover traffic-level attacks, which is the WAF's job.

## The constraint that matters most: NFR-12

`docs/compliance/nfr-12-exception.md` already records that personal data is held outside India, and that counsel has not reviewed the exception. **Any edge WAF or proxy sits in the path of every request and response.** It sees the request bodies (names, e-mail addresses, phone numbers, sign-in codes, answers) and it terminates TLS. So it is another holder of personal data in transit. It would have to be added to the DPDP data map and covered by the processor review that counsel is already asked to do (`counsel-brief.md`, E14). Cloudflare's edge is global. The cloud WAFs below are regional only if the load balancer they sit on is in a region the owner has chosen.

## Options

Prices come from the sources noted. Where a figure could not be checked against the vendor's own page, it is marked **unverified**.

### A. No WAF; rely on the application controls

- **Cost:** $0 in licences.
- **Covers:** rate limits, headers, authorization, input validation, dependency and code checks in CI.
- **Does not cover:** managed rules for known attack signatures, bot traffic, layer-7 floods.
- **Data impact:** none new.
- **Consequence:** the NFR-5 WAF item is not met and must be recorded as an accepted exception.

### B. Cloudflare in front of Render (proxy with the custom domain)

- **Free:** a managed ruleset based on OWASP. Custom rule limits differ between sources (**unverified**: one source says 5 custom rules, another says none).
- **Pro:** about $20 a month billed annually, or $25 billed monthly; includes the OWASP Core managed ruleset and 20 custom WAF rules (**unverified**, from third-party pricing aggregators).
- **Business:** about $200 a month billed annually, or $250 billed monthly; up to 100 custom WAF rules (**unverified**, aggregators).
- **Enterprise:** priced by contract (no figure found).
- **Set-up work:** move the domain's DNS to Cloudflare; configure the origin so the edge connects to Render over TLS; reconcile `ForwardedHeaders` (the API trusts only Render's proxy range, so the client address will come from Cloudflare's headers and the trust list must change too); update the static-site CSP `connect-src` if the API domain changes.
- **Data impact:** a new processor on every request. It needs the NFR-12 treatment above.
- **Check on the vendor page before relying on these figures:** cloudflare.com/plans.

### C. Google Cloud Armor (cloud load balancer in front of the API)

- **Pricing from Google's pricing page (verified through a search summary):** $5 per security policy per month, $1 per rule per month, $0.75 per million request evaluations for a global policy, $0.60 per million for a regional policy. Standard tier, no time commitment.
- **Not in these figures:** the external Application Load Balancer, its forwarding rules and the data-processing fee for traffic to the internet. A third-party source gives $0.075 per GB as a starting pay-as-you-go rate (**unverified**). The load balancer means moving the API off Render's direct service, so this is a hosting change, not a WAF change alone.
- **Data impact:** the traffic passes through Google's network; the regional policy keeps inspection in the chosen region.

### D. AWS WAF (in front of a CloudFront distribution or a load balancer)

- **Pricing (from memory, not verified in this session):** $5 per web ACL per month, $1 per rule per month, $0.60 per million requests. Confirm on AWS's pricing page.
- **Not in these figures:** the CloudFront or load balancer in front, and any Bot Control or CAPTCHA add-on.
- **Consequence:** the same as C. The API would sit behind AWS, so this is a hosting change.

### E. Azure Front Door with its WAF policy

Not priced here. It is the same kind of change as C and D: it fronts the API with a cloud the platform does not currently use.

## Recommendation (engineering view, for the owner to decide)

1. Record the decision with counsel first. Option B adds a processor, and the NFR-12 exception is still unreviewed. Counsel's answer decides whether B is allowed for personal data.
2. If counsel accepts it, option B (Free or Pro) is the smallest change that gives managed rules without moving hosting. Business is the figure to plan for if the owner wants custom rules for exam-day traffic.
3. If counsel does not accept an edge provider holding personal data, option A stays, and the NFR-5 WAF item is recorded as an accepted exception with its reason. Options C, D and E add hosting cost and a migration, and should be chosen only if the owner is also moving hosting.

Whichever option is chosen, add the WAF's own logs to the log-retention rules (CERT-In, counsel question 15) and update the data map.

## Owner's decision

| Question | Answer |
|---|---|
| Option chosen (A, B, C, D or E) | |
| Counsel's view on the processor, recorded on issue #11 | |
| Plan and budget for the chosen option | |
| Who creates and owns the vendor account | |
| Date of the decision | |
