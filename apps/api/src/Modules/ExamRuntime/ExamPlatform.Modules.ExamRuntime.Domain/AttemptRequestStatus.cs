namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>Where a candidate's request for another attempt stands.</summary>
public enum AttemptRequestStatus
{
    /// <summary>Waiting for an administrator.</summary>
    Pending,

    /// <summary>An administrator gave the candidate another attempt.</summary>
    Approved,

    /// <summary>An administrator turned the request down.</summary>
    Declined,
}
