namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>Where a candidate's report of a problem stands (FR-42).</summary>
public enum IssueReportStatus
{
    /// <summary>Waiting for staff.</summary>
    Open,

    /// <summary>Staff dealt with it, or decided nothing needs doing.</summary>
    Resolved,
}
