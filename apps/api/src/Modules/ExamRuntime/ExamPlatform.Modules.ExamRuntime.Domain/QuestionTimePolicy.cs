namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// Whether time per question may be stored and shown for attempts sat by candidates under 18 (FR-36, exam-platform-requirements.md section 7).
/// Off until counsel's opinion is recorded on issue #11: the configuration switch <c>ExamRuntime:TimePerQuestion:MinorsEnabled</c> defaults to
/// off, and only an operator who has the opinion and a recorded owner decision turns it on.
/// </summary>
/// <param name="MinorsEnabled">True when time per question is kept for attempts by minors; false withholds it.</param>
public sealed record QuestionTimePolicy(bool MinorsEnabled);
