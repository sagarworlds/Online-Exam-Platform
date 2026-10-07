# Functionality built beyond the original requirements

This lists what the platform does that [`exam-platform-requirements.md`](../exam-platform-requirements.md) does not ask for, or asks for in a narrower way, so that anyone reading the requirements can see what else is in the product, why it is there, and what needs the owner's decision. It was compiled on 2026-10-07 from `main` after pull request #158.

**How "not in the requirements" was decided.** Each item was checked against the functional requirements (FR-1 to FR-53), the non-functional requirements (NFR-1 to NFR-13), the content hierarchy (section 4), the data model (section 10), the API outline (section 13), the milestones (section 15) and the open questions (section 17). Items fall into three kinds:

| Kind | Meaning |
|---|---|
| **New** | No requirement asks for it. It may sit next to one. |
| **Beyond the wording** | A requirement covers the area; the platform adds a rule or a capability the text does not give. |
| **Supporting work** | Not a product feature: hosting, vendors, automation and safety nets around it. |

Things the requirements do ask for are not listed here, however they were done. Section 15 says a pull request lists the requirement IDs it covers and any assumptions it makes; the items below are the ones with no requirement ID to cite, so each says where it came from.

## At a glance

| # | Functionality | Kind | Nearest requirement | Pull requests | Status |
|---|---|---|---|---|---|
| N1 | Books and chapters for filing questions, and limiting an exam to a book or chapters | New | Section 4 (chapter is only a metadata field), section 10 | #97 | Merged |
| N2 | WhatsApp: sign-in codes to phones, delivery reports, one master off switch | New | FR-1, FR-39 (e-mail, SMS, in-app only) | #156, #157 | Merged, off by default |
| N3 | An invitation's exam code also sent on WhatsApp to the invited account's phone | New | FR-39 (invite notice), FR-50a | #157 | Merged, off by default |
| N4 | Copy an invite code to hand over by hand | New | FR-50a | #158 | Merged |
| N5 | Public privacy policy page | New | Section 7, FR-44, FR-48 | #154 | Merged, needs contact details |
| N6 | Extra attempts given by staff, and candidates asking for another attempt | New | FR-12 (attempt and retake limits only) | #97, #113, #115, #119 | Merged |
| N7 | Staff view of a candidate's marked paper | New | FR-29, FR-32 | #122, #148 | Merged |
| N8 | Admin page that shows unspent sign-in codes, for support | New | FR-1, FR-3 | #123 | Merged |
| N9 | Question bank search and paging | New | FR-5 | #116, #108 | Merged |
| N10 | Admin sidebar, API status dot, loading cue | New | None | #126, #129, #134, #135 | Merged |
| B1 | Marathi as a third language | Beyond the wording | FR-10, FR-51, open question 3 | #152, #153 | Merged |
| B2 | Pinned options, and the shuffling rules | Beyond the wording | FR-12, FR-28 | #106, #107 | Merged |
| B3 | Putting a draft exam right, and deleting a draft | Beyond the wording | FR-11, FR-12 | #98 | Merged |
| B4 | What may change about a question that is in use | Beyond the wording | FR-5, FR-7 | #97, #131 | Merged |
| B5 | Re-scoring one attempt | Beyond the wording | FR-31 | #149 | Merged |
| B6 | Staff view of where an attempt was sat from | Beyond the wording | FR-26 | #141, #150 | Merged |
| B7 | One active session per account at all times | Beyond the wording | FR-4 | #97 | Merged |
| S1 | Hosting on Render and Neon free plans | Supporting work | NFR-12, M1 | #124, #127 | Merged |
| S2 | E-mail through SMTP or Brevo | Supporting work | FR-39 | #118, #124 | Merged |
| S3 | Project board automation and hand-off notes | Supporting work | None | #94, #95 | Merged |
| S4 | Start-up refusals and consistency tests | Supporting work | NFR-5, NFR-11 | #155, #156 | Merged |

## New functionality

**N1. Books and chapters, and limiting an exam to them.** Questions can be filed under a chapter of a book, and an exam can be limited to one whole book or to chosen chapters of one book. The requirements name "chapter" only as one metadata field of a question (section 4) and their data model has a Subject/Topic tree with no Book or Chapter; nothing limits where an exam's questions may come from. The platform has admin pages for books and chapters, filing one or many questions at once, filtering the question list by book or chapter, and an API rule that refuses a question outside an exam's scope. Books and chapters are archived, never deleted. See [Books, chapters and exam scope](../README.md#books-chapters-and-exam-scope).

**N2. WhatsApp: sign-in codes, delivery reports, one master switch.** A candidate who registers or signs in with a phone number gets the one-time code as a WhatsApp message through Meta's Cloud API. FR-1 asks for e-mail or phone codes and FR-39 for e-mail, SMS and in-app notices; WhatsApp is not mentioned anywhere. It comes with the webhook Meta needs (`/v1/webhooks/whatsapp`, which checks a signature and logs delivery reports with numbers masked) and one master switch, `WhatsApp__Enabled`, off unless set to true, which stops every WhatsApp message whatever else is configured. See [WhatsApp (Meta Cloud API)](../README.md#whatsapp-meta-cloud-api).

**N3. An invitation's exam code on WhatsApp.** When an invitation is created for an address that belongs to an account with a phone number, the code the candidate enters on the invitation page is also sent to that phone, through an approved template. It needs the master switch and `Invite__WhatsApp__TemplateName`. See [Invitations on WhatsApp](../README.md#invitations-on-whatsapp).

**N4. Copy an invite code to hand over by hand.** Staff can copy a fresh single-use invite code, and the link that carries it, for any pending invitation, to give to a candidate over a call or in a chat. The requirements describe single-use codes with expiry and revocation (FR-50a), not a way for staff to hand one over. Each code handed over is audited without the code itself. The existing `POST /v1/invites/{id}/codes` now also returns the link and refuses an invitation that is no longer pending. See [Handing an invite code over by hand](../README.md#handing-an-invite-code-over-by-hand).

**N5. Public privacy policy page.** A static page at `/privacy.html`, readable without signing in or JavaScript, which Meta's app review asks for. Section 7 and FR-44, FR-46 and FR-48 ask for a consent ledger, consent text generated from the proctoring profile and a grievance contact, not a public policy page. It must be given the operator's name and a grievance e-mail before it is used, and counsel should review it. See [Privacy policy page](../README.md#privacy-policy-page-public).

**N6. Extra attempts and attempt requests.** FR-12 asks for attempt and retake limits as an exam setting. Beyond that, staff can give one candidate another attempt; a candidate who has used all theirs can ask for one with a reason; staff approve or decline from a queue; staff are e-mailed when a request arrives and the candidate when it is decided; and My exams marks the highest-scoring submitted attempt as Best (nothing is stored as the exam's official score). The notices are not among the events FR-39 lists. See [Extra attempts](../README.md#extra-attempts).

**N7. Staff view of a candidate's marked paper.** On Candidates and attempts, **Show paper** opens the paper one attempt consisted of, in the candidate's order and at the version they began with, with the marks each question earned and the correct answer marked. FR-32 is the candidate's own result page and FR-29 lists admin actions on an attempt, not reading the paper. See [The marked paper, for staff](../README.md#the-marked-paper-for-staff).

**N8. Admin page that shows unspent sign-in codes.** A page for support, behind the `identity.otp.read` permission, listing the unspent sign-in and registration codes of candidates; the views are audited. The requirements do not ask for staff to be able to read a user's code.

**N9. Question bank search and paging.** Full-text search of questions, and a "Load older" button instead of loading the whole bank. FR-5 asks for create, read, update and delete; finding and paging are not specified.

**N10. Admin sidebar, API status dot, loading cue.** Admin navigation moved to a sidebar with drawn icons, a small coloured dot shows whether the API is answering, and a calm loading indicator appears while it is slow to answer (a free-plan service sleeps when idle). None is a stated requirement.

## Beyond the wording of a requirement

**B1. Marathi.** FR-10 and FR-51 say "English + Hindi first", extensible, and open question 3 asks which regional languages come next. Marathi (`mr`) is now a third interface language and question language, so this answers that open question. The board records it as a decision: issue #31 was retitled from "EN + HI" to "EN + HI + MR" on 7 October 2026, shortly before the pull request. The requirements document itself still says English and Hindi. See [Interface and content language](../README.md#interface-and-content-language-fr-51) and [Languages and linked translations](../README.md#languages-and-linked-translations-fr-10).

**B2. Pinned options and the shuffling rules.** FR-12 and FR-28 ask for question and option shuffling, seeded per candidate. Added on top: an option can be pinned ("keep in place", such as a "none of the above" that must stay last); shuffling is off by default; and a later attempt at an exam shows questions and options shuffled even when the first did not. See [Extra attempts](../README.md#extra-attempts).

**B3. Putting a draft exam right.** FR-11 and FR-12 ask for building and configuring an exam. Added: taking a question or section out of a draft, renaming a section, correcting the name and description (also after publishing, since nothing asked or scored changes), and deleting a draft, which the other modules can refuse. See [Putting a draft exam right](../README.md#putting-a-draft-exam-right).

**B4. What may change about a question that is in use.** FR-5 asks for create, read, update and delete and FR-7 for versions that never alter past attempts. The platform's reading of them: once a candidate has answered a question only its wording can change, and a question cannot be deleted while any exam holds it. See [Editing, deleting and filing questions](../README.md#editing-deleting-and-filing-questions).

**B5. Re-scoring one attempt.** FR-31 asks for rescoring all affected attempts when an answer key is revised (that route is covered by the requirement). Beyond it, staff can re-score a single submitted attempt from its stored answers, with a reason, and a changed score is kept as a revision the candidate sees. It was added to repair a real defect: a submit racing an answer save once left a score one question short (#149). See [Saving answers and submitting at the same moment](../README.md#saving-answers-and-submitting-at-the-same-moment).

**B6. Staff view of where an attempt was sat from.** FR-26 asks for IP and device fingerprint logging and multi-login detection. The platform also shows staff, per attempt, how many devices and address changes there were and a list of sign-in details (address, short device signature, when). Candidates never see it. See [Where an attempt is sat from](../README.md#where-an-attempt-is-sat-from-ip-address-device-signature-and-multi-login-detection).

**B7. One active session per account at all times.** FR-4 says one active session per candidate *during an exam*. The platform ends an account's earlier session whenever it signs in again, for staff too, whether or not an exam is running.

## Supporting work

**S1. Hosting on Render and Neon free plans.** The live deployment runs on Render's free web service and a free Neon database (`render.yaml`, [`docs/deploy-render.md`](./deploy-render.md)). NFR-12 asks for India-region hosting and M1 for India-region infrastructure as code; ADR 0001 records that as blocked on open question 4 (which cloud provider). The free plans sleep when idle and the database holds 0.5 GB, so this is a stand-in for trying the platform, not the hosting the requirements describe.

**S2. E-mail through SMTP or Brevo.** Mail goes through one shared sender with two adapters: SMTP, and Brevo's HTTPS API for hosts that block outbound SMTP. FR-39 asks for e-mail; the requirements do not choose how or from whom.

**S3. Project board automation and hand-off notes.** A GitHub Actions job keeps the project board in step with issue and pull request activity, and `docs/handoff/` holds the stabilisation plan. Process, not product.

**S4. Start-up refusals, safety nets and developer helpers.** The host refuses to start without a one-time-code provider (and without WhatsApp's settings once WhatsApp is switched on), and refuses a log-only code sender outside Development, so a code can never end up in a production log. Asking for a code for an unknown or locked address gets the same answer as a real request (a decoy challenge is stored), so replies do not reveal who has an account. A test fails if a module's migrations stop describing its model, and architecture tests keep the module boundaries. In Development, codes are written (masked) to the log and the log says why a sign-in got none, and a first administrator can be created from user-secrets. These support NFR-5, NFR-6 and NFR-11 rather than add behaviour.

## Decisions waiting for the owner

1. **WhatsApp consent (N2, N3).** The privacy page says WhatsApp notices go only to people who agreed to them, and WhatsApp's own policy requires the same, but the platform records no such agreement (there is no consent purpose for it). Everything ships off, and turning it on is the operator's statement that those people agreed. Opt-in and opt-out records are the next piece of FR-39.
2. **The unspent-codes page (N8).** It shows credentials to anyone holding `identity.otp.read`. Decide whether it should exist in production, especially now that codes can be delivered on WhatsApp.
3. **Hosting region (S1).** Moving to an India region is the open question 4 the requirements left for the owner.
4. **Policy choices made while the requirements were silent (N6, B2, B4).** Marking the best attempt, shuffling later attempts, and locking a question's answers once answered are reasonable, but they are choices; confirm or change them.

## Keeping this list current

When a pull request adds something no requirement covers, add a row to the table above and a paragraph below it in the same pull request, and say in the description which requirement it sits next to. When a requirement is later written for one of these, move it out of this file and cite the requirement instead.
