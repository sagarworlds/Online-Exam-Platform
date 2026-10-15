# Cover note for counsel

**Decision:** D1 in `docs/owner-decisions.md`, answered 2026-10-15. Send counsel the whole brief now, and ask for the four groups that gate the build and the hosting first.
**For the owner:** fill in the angle brackets, read the note once, and send it with the files listed at the end. Everything in it comes from the repository. Nothing in it is legal advice, and it says so to counsel too.

---

Subject: Online exam platform: request for an opinion on children's data, consent, monitoring, retention and data location

Dear <counsel's name>,

I am building an online exam platform for schools and coaching centres in India, and I would like your opinion before it handles real candidates' data. It is invite-only. Candidates sit timed exams in a browser. The platform stores their accounts (name, e-mail or phone, date of birth), answers and results, and some activity data (the device and network they sat from, and whether they left the exam page). It runs on Render (hosting) and Neon (database), and sends e-mail through Brevo and, when switched on, WhatsApp messages through Meta. The processors sit outside India today. I have recorded that as an exception and I need to know whether it is acceptable.

**The first pilot is for adult candidates only.** Children come later, after your opinion on them.

I attach a brief with **18 questions**, in seven groups (A to G). Each question says what the platform does, so you can answer without reading code. These decide what I build and where I host, so I would be grateful for them first:

- **Group A,** classification and monitoring of children (questions 1 to 4)
- **Group B,** consent for children (questions 5 to 7)
- **Questions 14 and 15** in group E: processors and cross-border transfer, and Indian regions with the 180-day log rule

The rest can follow in your own time: retention (8 to 10), sensitive data (11 and 12), the grievance officer (13), the notices (16), an edge provider in front of the platform (17) and the date of birth (18). If it is easier, answer in the order of the brief.

I would like the first answers by **<date>**. Please tell me if that is not realistic and what you would need.

Please answer with one line per question, A1 to G18, giving the answer or "not answered", and note anything you make a condition. I will record your opinion, with the date and your name, on the issue that tracks it.

Two things to know:

- The notices (terms of use, privacy notice, a notice for proctoring data, and two proposed ones) are drafts in English and Hindi. A Marathi version will follow once you have settled the English and Hindi. Please mark any text that is not accurate for this platform.
- The retention periods in the data map are open. I have proposed starting points for you to confirm or change (`retention-proposal.md`).

Thank you.

<owner's name>
<organisation, address, phone, e-mail>

---

## Files to attach

| File | What it is |
|---|---|
| `docs/compliance/counsel-brief.md` | The 18 questions |
| `docs/compliance/dpdp-data-map.md` | Every item of personal data the platform stores or sends, where it goes, and the open points |
| `docs/compliance/consent-notices.json` | The draft notices |
| `docs/compliance/nfr-12-exception.md` | The data-location exception, not yet reviewed |
| `docs/compliance/minors-time-per-question-exception.md` | The exception for recording time per question for candidates under 18 |
| `docs/compliance/retention-proposal.md` | Proposed retention periods, for you to confirm |
| `docs/compliance/waf-decision.md` | The web application firewall options (question 17) |
| `docs/observability.md` | What the platform logs, and the 180-day log-retention setting (question 15) |

If sending the repository's files one by one is a problem, export each to PDF first.

## When the opinion comes back

1. Record it on [#11](https://github.com/sagarworlds/Online-Exam-Platform/issues/11) as the brief asks: the date, who gave it, one line per question, the conditions with an owner for each, and a link to the file or letter.
2. Tell engineering. The answers decide: whether FR-24, FR-25 and FR-27 can be built for under-18 candidates, the retention job (#79), the access rules and region settings, the web application firewall, the date-of-birth field, and the consent notices (#269).
3. Close #11. Update the data map and the notices draft to match.
