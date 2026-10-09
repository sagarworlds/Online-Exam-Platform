namespace ExamPlatform.Modules.ExamRuntime.Endpoints.Notifications;

/// <summary>The <c>Notifications</c> settings: how the platform's scheduled e-mails (FR-39) are triggered.</summary>
public sealed class NotificationOptions
{
    /// <summary>The configuration section these settings are read from.</summary>
    public const string SectionName = "Notifications";

    /// <summary>The shortest run key accepted, so the trigger is not guarded by something guessable.</summary>
    public const int MinRunKeyLength = 24;

    /// <summary>The most minutes allowed between passes of the timer inside the host; a day.</summary>
    public const int MaxPollMinutes = 1440;

    /// <summary>
    /// Whether the host runs the notification pass on a timer of its own. On by default. A host that sleeps when idle (as a free Render
    /// service does) cannot rely on it, which is what <see cref="RunKey"/> and an outside scheduler are for; both may be used, because a pass
    /// that overlaps another is skipped and nothing is sent twice.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minutes between the timer's passes; five by default.</summary>
    public int PollMinutes { get; set; } = 5;

    /// <summary>
    /// The secret that lets an outside scheduler start a pass by calling <c>POST /v1/notifications/run</c> with it in the
    /// <c>X-Notifications-Key</c> header, which also wakes a host that sleeps. Without it the route answers 404, as if not there.
    /// </summary>
    public string? RunKey { get; set; }
}
