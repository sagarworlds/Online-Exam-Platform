namespace ExamPlatform.Modules.Proctoring.Domain;

/// <summary>
/// The kinds of evidence the risk score reads (FR-27). Each one is a fact the platform already records, so the score adds no new
/// monitoring: no camera, no screen, nothing a candidate's device was not already reporting (FR-24, FR-25 are not built).
/// </summary>
public enum RiskSignalKind
{
    /// <summary>How many times the candidate left the exam page (FR-22).</summary>
    FocusDepartures,

    /// <summary>How many times the candidate's address or device changed during the attempt (FR-26).</summary>
    ClientChanges,

    /// <summary>Whether an administrator has invalidated the result (FR-29). A decision already taken by a person, read as evidence.</summary>
    Invalidated,

    /// <summary>How many seconds the attempt took per answered question. Judged only once enough questions were answered.</summary>
    FastCompletion,

    /// <summary>How many identical, rarely given wrong answers the attempt shares with one other candidate of the same exam.</summary>
    SharedWrongAnswers,
}
