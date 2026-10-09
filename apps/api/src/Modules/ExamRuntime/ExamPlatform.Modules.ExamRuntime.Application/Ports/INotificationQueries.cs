namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>A submitted attempt whose candidate has not yet been told their result is out, as far as the notification run needs to know it.</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate.</param>
/// <param name="Number">Which attempt it is for the candidate at the exam, from 1.</param>
/// <param name="SubmittedAtUtc">When it was submitted.</param>
public sealed record SubmittedAttemptRow(Guid AttemptId, Guid ExamId, Guid CandidateId, int Number, DateTime SubmittedAtUtc);

/// <summary>A revision of a score whose candidate has not yet been told, with what the message says.</summary>
/// <param name="RevisionId">The revision.</param>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate.</param>
/// <param name="SubmittedAtUtc">When the attempt was submitted.</param>
/// <param name="PreviousScore">The score before.</param>
/// <param name="PreviousMaxScore">The marks available before.</param>
/// <param name="NewScore">The score after.</param>
/// <param name="NewMaxScore">The marks available after.</param>
/// <param name="Reason">Why it changed, as staff gave it.</param>
/// <param name="RevisedAtUtc">When it changed.</param>
public sealed record RevisionRow(
    Guid RevisionId,
    Guid AttemptId,
    Guid ExamId,
    Guid CandidateId,
    DateTime SubmittedAtUtc,
    decimal PreviousScore,
    decimal PreviousMaxScore,
    decimal NewScore,
    decimal NewMaxScore,
    string Reason,
    DateTime RevisedAtUtc);

/// <summary>
/// The two questions the notification run asks of the attempts, answered with a few columns and without loading the attempts, since it
/// asks them every few minutes. Both leave out what was already sent, so what comes back is only what still needs looking at.
/// </summary>
public interface INotificationQueries
{
    /// <summary>
    /// Submitted, not invalidated attempts submitted after <paramref name="submittedAfterUtc"/> whose result e-mail is neither sent nor given up on.
    /// Whether their result is out yet depends on the exam, which the caller reads.
    /// </summary>
    /// <param name="submittedAfterUtc">How far back to look.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SubmittedAttemptRow>> ListAwaitingResultNoticeAsync(DateTime submittedAfterUtc, CancellationToken cancellationToken);

    /// <summary>Score revisions made after <paramref name="revisedAfterUtc"/>, of attempts that are not invalidated, whose e-mail is neither sent nor given up on.</summary>
    /// <param name="revisedAfterUtc">How far back to look.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<RevisionRow>> ListAwaitingRevisionNoticeAsync(DateTime revisedAfterUtc, CancellationToken cancellationToken);
}
