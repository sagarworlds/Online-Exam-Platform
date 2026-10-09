namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>Where a candidate's dispute of an answer key stands (FR-31).</summary>
public enum DisputeStatus
{
    /// <summary>Waiting for staff.</summary>
    Open,

    /// <summary>Staff corrected the question's answer key, so the candidate's result was rescored under it.</summary>
    Accepted,

    /// <summary>Staff looked at it and left the answer key as it is.</summary>
    Rejected,
}
