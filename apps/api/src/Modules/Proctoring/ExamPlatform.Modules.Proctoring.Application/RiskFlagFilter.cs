namespace ExamPlatform.Modules.Proctoring.Application;

/// <summary>Which flagged assessments the review queue shows.</summary>
public enum RiskFlagFilter
{
    /// <summary>Flagged and not yet decided: the queue a reviewer works through. The default.</summary>
    Open,

    /// <summary>Flagged and marked reviewed.</summary>
    Reviewed,

    /// <summary>Flagged and dismissed.</summary>
    Dismissed,

    /// <summary>Flagged, whatever the decision.</summary>
    All,
}
