namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>A reminder that an exam is about to start.</summary>
/// <param name="To">The candidate's address.</param>
/// <param name="ExamName">The exam.</param>
/// <param name="StartUtc">When it starts.</param>
/// <param name="WithinAnHour">Whether it starts within the hour (otherwise within the day).</param>
public sealed record ExamReminderEmail(string To, string ExamName, DateTime StartUtc, bool WithinAnHour);

/// <summary>The news that a candidate's result can be seen.</summary>
/// <param name="To">The candidate's address.</param>
/// <param name="ExamName">The exam.</param>
/// <param name="AttemptNumber">Which attempt it is for the candidate, from 1.</param>
public sealed record ResultReleasedEmail(string To, string ExamName, int AttemptNumber);

/// <summary>The news that a candidate's score changed after they saw it.</summary>
/// <param name="To">The candidate's address.</param>
/// <param name="ExamName">The exam.</param>
/// <param name="PreviousScore">The score before.</param>
/// <param name="PreviousMaxScore">The marks available before.</param>
/// <param name="NewScore">The score after.</param>
/// <param name="NewMaxScore">The marks available after.</param>
/// <param name="Reason">Why it changed, as staff gave it.</param>
public sealed record ScoreRevisedEmail(
    string To, string ExamName, decimal PreviousScore, decimal PreviousMaxScore, decimal NewScore, decimal NewMaxScore, string Reason);

/// <summary>
/// Hands the platform's scheduled e-mails to the mail server (FR-39). Each method answers whether the mail server took the message and
/// never throws for a refusal or an outage: the run records the try and goes on, and tries again later.
/// </summary>
public interface IExamNotificationMailer
{
    /// <summary>Whether there is a mail server to send to at all. With none, the run waits and sends what is due once there is one.</summary>
    bool IsAvailable { get; }

    /// <summary>Sends a reminder that an exam is about to start.</summary>
    /// <param name="email">What to say and to whom.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> SendReminderAsync(ExamReminderEmail email, CancellationToken cancellationToken);

    /// <summary>Tells a candidate their result can be seen.</summary>
    /// <param name="email">What to say and to whom.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> SendResultReleasedAsync(ResultReleasedEmail email, CancellationToken cancellationToken);

    /// <summary>Tells a candidate their score changed.</summary>
    /// <param name="email">What to say and to whom.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> SendScoreRevisedAsync(ScoreRevisedEmail email, CancellationToken cancellationToken);
}
