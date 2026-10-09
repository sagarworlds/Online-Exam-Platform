# NFR-12 exception: personal data held outside India

**Requirement:** NFR-12, data location: India-region hosting; cross-border transfer off by default. Section 7.3 asks for Indian cloud regions, with the region configurable.
**Issue:** [#22, India-region infrastructure as code](https://github.com/sagarworlds/Online-Exam-Platform/issues/22) (milestone M1).
**Decision:** the product owner chose to keep the current providers and record this exception, rather than move to an Indian cloud provider.
**Status:** accepted as a deviation by the product owner. **Not reviewed by counsel.** Section 7.3 treats data location as a legal requirement, so counsel must confirm this exception before any real candidate data is stored.

## Why this exception exists

No India region was found for the two providers the platform runs on:

- **Render** (API and website, `render.yaml`) offers Oregon, Ohio, Virginia, Frankfurt and Singapore, per a search summary of [Render's region list](https://render.com/docs/regions).
- **Neon** (Postgres, `docs/deploy-render.md`) offers US East, US West, Frankfurt, Singapore and Sydney, per a search summary of [Neon's region list](https://neon.tech/docs/introduction/regions). A Neon project's region cannot be changed after it is created.

Both lists were taken from search summaries, because the provider pages could not be opened from the drafting environment. Confirm them in the Render and Neon consoles before relying on this record.

## What is held outside India

The full inventory is in the [DPDP data map](dpdp-data-map.md). In short:

| Holder | Data | Location | Cross-border? |
|---|---|---|---|
| Neon (database) | Every personal data row: accounts, consent records, attempts and answers, guardian links, invitations | Operator's choice of the regions above | Yes |
| Render (API, logs) | Every request and response, and application logs | Not set in `render.yaml` | Yes |
| Brevo (e-mail) | E-mail addresses, sign-in codes, invitations | EU, per Brevo's help article | Yes |
| Meta WhatsApp (sign-in codes, invitations) | Phone numbers and message content | United States by default. India local storage can be enabled per phone number | Yes, unless local storage is enabled |

NFR-12 says cross-border transfer is off by default. This exception turns it on for every row above. Counsel must accept that for each category, and especially for candidates under 18 (issue [#11](https://github.com/sagarworlds/Online-Exam-Platform/issues/11), counsel brief).

## Conditions to record before launch

1. Counsel confirms in writing that holding this data outside India is acceptable under the DPDP Act and Rules for each category, and for children's data in particular.
2. Enable India local storage on the WhatsApp business number, and record that it is on.
3. Record the chosen Render and Neon regions here and in `render.yaml` / `docs/deploy-render.md`, so the choice is written down.
4. Confirm the Brevo data-processing agreement and the Meta terms (the processor sources are summaries, not contracts).
5. Review this exception before M8 hardening, or sooner if an Indian region becomes available for either provider.

## What this changes in code

Nothing. No infrastructure-as-code is written for #22, because there is no Indian region to target. The region settings stay as they are.
