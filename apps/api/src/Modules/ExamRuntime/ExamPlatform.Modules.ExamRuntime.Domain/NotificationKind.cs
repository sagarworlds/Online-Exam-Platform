namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>The e-mails the platform sends on its own, on a schedule or because something changed (FR-39).</summary>
public enum NotificationKind
{
    /// <summary>The exam starts within a day: sent to each enrolled candidate once the exam is closer than 24 hours.</summary>
    ExamReminder24Hours,

    /// <summary>The exam starts within the hour: sent to each enrolled candidate once the exam is closer than an hour.</summary>
    ExamReminderOneHour,

    /// <summary>A candidate's result can now be seen: the subject is the attempt.</summary>
    ResultReleased,

    /// <summary>A candidate's score was revised after they saw it: the subject is the revision.</summary>
    ScoreRevised,
}
