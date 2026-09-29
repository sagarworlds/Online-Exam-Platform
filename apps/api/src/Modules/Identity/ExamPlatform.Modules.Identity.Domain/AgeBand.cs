namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>
/// Coarse age classification derived from date of birth. Per
/// exam-platform-requirements.md section 7.1, "a child is anyone under 18" —
/// this drives guardian-consent gating and default proctoring posture elsewhere
/// in the platform, so it is computed once here rather than recomputed ad hoc.
/// </summary>
public enum AgeBand
{
    /// <summary>Under 18 years old.</summary>
    Minor,

    /// <summary>18 years old or older.</summary>
    Adult
}
