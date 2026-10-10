namespace ExamPlatform.Modules.Notifications.Contracts;

/// <summary>What an in-app notice is about. Its members mirror the Domain's <c>NoticeKind</c>, which stays internal to the module.</summary>
public enum InAppNoticeKind
{
    /// <summary>An invitation to an exam was sent to an address that belongs to an account.</summary>
    InviteReceived,

    /// <summary>A candidate asked for another attempt; staff who can answer it are told.</summary>
    AttemptRequestReceived,

    /// <summary>A request for another attempt was granted.</summary>
    AttemptRequestApproved,

    /// <summary>A request for another attempt was turned down.</summary>
    AttemptRequestDeclined,

    /// <summary>A dispute of an answer key was turned down; the key stands.</summary>
    DisputeRejected,

    /// <summary>An exam starts within a day.</summary>
    ExamReminder24Hours,

    /// <summary>An exam starts within the hour.</summary>
    ExamReminderOneHour,

    /// <summary>A candidate's result can be seen.</summary>
    ResultReleased,

    /// <summary>A candidate's score was revised after they saw it.</summary>
    ScoreRevised,
}

/// <summary>
/// One in-app notice to record: who it is for, what it is about, and the thing it happened to.
/// </summary>
/// <param name="RecipientUserId">The signed-in account the notice is for. Guardians have no account and are never a recipient.</param>
/// <param name="Kind">The event the notice is about.</param>
/// <param name="SubjectId">The invite, attempt, request, dispute, exam or score revision the event happened to, according to the kind.</param>
/// <param name="ExamName">The exam's name for the feed, or null for a notice that is not about an exam.</param>
public sealed record InAppNotice(Guid RecipientUserId, InAppNoticeKind Kind, Guid SubjectId, string? ExamName);
