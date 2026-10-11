namespace ExamPlatform.Modules.Notifications.Domain;

/// <summary>
/// What an in-app notice is about: the event that raised it. The feed words each kind, and the store keys a notice by its kind, so one event
/// is told once. Its members mirror <c>InAppNoticeKind</c> in the Contracts project, which is what other modules name.
/// </summary>
public enum NoticeKind
{
    /// <summary>An invitation to an exam was sent to an address that belongs to an account (FR-14).</summary>
    InviteReceived,

    /// <summary>A candidate asked for another attempt, so staff who can answer it are told.</summary>
    AttemptRequestReceived,

    /// <summary>A request for another attempt was granted.</summary>
    AttemptRequestApproved,

    /// <summary>A request for another attempt was turned down.</summary>
    AttemptRequestDeclined,

    /// <summary>A dispute of an answer key was turned down; the key stands (FR-31).</summary>
    DisputeRejected,

    /// <summary>A dispute of an answer key was accepted: the key was corrected and the candidate's result rescored under it (FR-31).</summary>
    DisputeAccepted,

    /// <summary>An exam starts within a day (FR-39).</summary>
    ExamReminder24Hours,

    /// <summary>An exam starts within the hour (FR-39).</summary>
    ExamReminderOneHour,

    /// <summary>A candidate's result can be seen (FR-39).</summary>
    ResultReleased,

    /// <summary>A candidate's score was revised after they saw it (FR-39).</summary>
    ScoreRevised,
}
