# Exception: time per question for candidates under 18

**Requirements:** section 7.1 (a child is anyone under 18) and section 7.2 (behavioural monitoring of minors; no automated profiling of minors until counsel's opinion).
**Feature:** FR-36 candidate analytics. Each attempt records how long the candidate spends on each question (`AttemptQuestionTimes`), and the candidate sees their own average time per question.
**Decision:** accepted by the product owner on 2026-10-12, with the default for candidates under 18 set to off. Time per question is not recorded for candidates under 18 unless the switch described below is turned on. Other candidates are unaffected.
**Counsel review:** **not reviewed by counsel.** The opinion on minors is still open on [issue #11](https://github.com/sagarworlds/Online-Exam-Platform/issues/11). This exception stands until counsel answers, and counsel may decide it has to be withdrawn.

## Default and switch

- **The default is off** for candidates under 18. While it is off, nothing is written for an attempt by a candidate under 18, and their own analytics show no time figures (not a zero). Until the switch is turned on, the coverage below applies to no candidate under 18.
- **The setting** is `ExamRuntime:TimePerQuestion:MinorsEnabled`, which defaults to `false`. It is documented in the README with the other Exam Runtime settings (FR-36). It is separate from `Proctoring:MinorsScanEnabled`, which is unchanged.
- **An unknown age counts as under 18.** A candidate with no date of birth on record is treated as a minor, so their time is not recorded while the switch is off.
- **Enabling the switch needs a recorded owner decision**, with its date, before it is turned on in any environment. Counsel's opinion is still open (see below).

## What the exception covers

- Recording time per question for candidates under 18, and showing it in the candidate's own analytics.

## What it does not cover

- **The risk scan (FR-27).** It reads each attempt's pace and leaves out candidates under 18 unless `Proctoring:MinorsScanEnabled` is turned on. That switch defaults to off, and this exception does not change it.
- **Other readers of the data.** Time per question is not read by the risk scan, by staff flags or by any ranking. This was checked on the branch: no code outside the Analytics and ExamRuntime modules reads it.
- **Camera, screen and face-presence features (FR-24, FR-25).** These need counsel's answer before they can be built for candidates under 18.

## Revisit when

- Counsel answers issue #11. Record the answer here, with the date and the counsel's name.
- Any change makes time per question an input to a flag, a risk score or a ranking.
