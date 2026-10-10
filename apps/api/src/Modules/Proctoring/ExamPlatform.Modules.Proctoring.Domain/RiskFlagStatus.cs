namespace ExamPlatform.Modules.Proctoring.Domain;

/// <summary>Where a risk assessment stands with the reviewer. Only a human moves it away from <see cref="Open"/> (FR-27).</summary>
public enum RiskFlagStatus
{
    /// <summary>No reviewer has decided yet. A flagged open assessment is waiting in the review queue.</summary>
    Open,

    /// <summary>A reviewer looked at the flagged attempt and marked it reviewed.</summary>
    Reviewed,

    /// <summary>A reviewer dismissed the flag, with a note saying why.</summary>
    Dismissed,
}
