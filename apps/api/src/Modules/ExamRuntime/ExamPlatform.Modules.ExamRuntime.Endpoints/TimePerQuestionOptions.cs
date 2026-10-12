namespace ExamPlatform.Modules.ExamRuntime.Endpoints;

/// <summary>
/// The <c>ExamRuntime:TimePerQuestion</c> settings: whether time per question is recorded and shown for candidates under 18 (FR-36,
/// exam-platform-requirements.md section 7). Off by default; see the compliance note in docs/compliance/minors-time-per-question-exception.md.
/// </summary>
public sealed class TimePerQuestionOptions
{
    /// <summary>The configuration section these settings are read from.</summary>
    public const string SectionName = "ExamRuntime:TimePerQuestion";

    /// <summary>
    /// Whether time per question is stored and shown for candidates under 18. False withholds it: nothing is written for their attempts, and
    /// their own analytics show no time. Turning it on needs a recorded owner decision and counsel's opinion (issue #11).
    /// </summary>
    public bool MinorsEnabled { get; set; }
}
