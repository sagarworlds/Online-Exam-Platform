# Counsel brief: minors and proctoring

**Milestone:** M0 Discovery & legal, issue [#11](https://github.com/sagarworlds/Online-Exam-Platform/issues/11) (label `blocked`: waiting on counsel).
**Purpose:** the questions the engineering team needs answered before building FR-24, FR-25 and FR-27, and before keeping the candidate-activity data listed in the [data map](dpdp-data-map.md). The opinion is recorded on issue #11 when it arrives, with the date, the counsel's name, and the answer to each question.

Requirements context: section 7 (India compliance) and section 8 (configurable proctoring). A child is anyone under 18 (section 7.1).

## What the platform does today, for counsel to assess

- Candidates can be under 18. The date of birth is stored and gates guardian consent (`Identity`, `Guardian`).
- During an attempt the platform records **focus violations** (the candidate left the exam page), **client sightings** (IP address and device fingerprint, and when the device or network changed), and **warnings** from staff. Each is kept per attempt and shown to reviewers and in disputes.
- **Accommodations** can carry free-text notes, which may describe a disability or health need.
- The camera, screen and face-presence features (FR-24, FR-25, FR-27) are **not built**. The proctoring profile is configuration only.

## Questions

### A. Classification and monitoring of children

1. Is the platform, or the school or coaching centre that runs an exam on it, the data fiduciary for candidates' data? Who carries the duties in section 7.1?
2. Does an educational exemption cover **behavioural monitoring of children** through focus violations and client sightings? If not, what should change?
3. Does a camera or screen feature (FR-24, FR-25, FR-27) for a child fall under the bar on behavioural monitoring? Is there any form of it that can be used for children?
4. Is "no automated profiling of minors" (section 7.2) met if staff review flags that the platform raised? Does a flag count as automated profiling?

### B. Consent for children

5. What counts as **verifiable parental consent** for the platform, given the age and identity signals it has? Is the guardian's e-mail or phone, verified by a one-time code, enough?
6. When a candidate turns 18, does their own consent replace the guardian's? What happens to the guardian link?
7. Is the proctoring default in section 7.2 right for under-18 candidates: off, or browser lock only? Should the platform block camera and screen features for them outright?

### C. Retention

8. How long may each of these be kept, and for what purpose: client sightings, focus violations, warnings, accommodation notes, issue reports, the consent record (proof of consent), and the audit log? The data map lists each.
9. Should the consent record outlive the account, so the platform can still prove consent? For how long?
10. What retention applies to proctoring media, if it is ever recorded: "N days after result finalisation" (section 7.3) — what should N be, and for which kinds of exam?

### D. Sensitive data

11. Accommodation notes can describe a disability or health need. What is the minimum this may hold? Who may read it, and is it kept apart from the exam results?
12. Staff can reveal a candidate's login or registration code when the candidate cannot receive one (`OtpPurposeExtensions.IsRevealableToStaff`). Is that lawful, and what must it record?

### E. Data-principal rights, processors and location

13. Name the grievance officer the notices need, and state what the officer must answer and within how long.
14. The data goes to Render (hosting), Neon (database), Brevo (e-mail) and Meta WhatsApp. Which of these need a data-processing agreement? Is any cross-border transfer allowed for each?
15. Section 7.3 asks for Indian cloud regions. Must the database, the backups and the logs stay in India, and does the CERT-In 180-day log rule apply to the application logs?

### F. The notices

16. Review the three draft notices in [consent-notices-draft.md](consent-notices-draft.md): terms of service, privacy notice, and proctoring data processing. Note any text that is not accurate for this platform.

## What the engineering team does with the answers

- Answers to A and B decide whether FR-24, FR-25 and FR-27 can be built for under-18 candidates, and in which form. They are a precondition for M6.
- Answers to C set the retention jobs (FR-47), which are not built.
- Answers to D and E set the access rules for accommodation notes and the region settings in `render.yaml` and the database configuration.
- Answer 16 is the last step before the consent notices are seeded with real addresses (see the open list in the notices draft).

## Recording the opinion

When the opinion arrives, record it on issue #11 with:

- the date, and who gave it (counsel's name and firm);
- one line per question, A1 to F16, with the answer or "not answered";
- anything the opinion makes a condition, with an owner for each condition;
- the link to the file or letter.

Then close #11 and update the data map and the notices draft to match.
