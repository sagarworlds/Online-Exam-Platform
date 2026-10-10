namespace ExamPlatform.Modules.Proctoring.Endpoints;

/// <summary>
/// The Proctoring module's switches, bound from the <c>Proctoring</c> configuration section.
/// </summary>
public sealed class ProctoringOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Proctoring";

    /// <summary>
    /// Whether the risk score may read attempts sat by candidates under 18. Off by default: counsel's opinion (issue #11) is a precondition
    /// for scoring minors (exam-platform-requirements.md section 7.2), and until it is recorded no environment turns this on.
    /// </summary>
    public bool MinorsScanEnabled { get; set; }
}
