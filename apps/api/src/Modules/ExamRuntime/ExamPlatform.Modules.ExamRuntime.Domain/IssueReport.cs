using ExamPlatform.Modules.ExamRuntime.Domain.Events;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// A problem a candidate reported from inside an exam (FR-42): a question that does not make sense, a page that does not work, or
/// anything else. It only tells staff; it changes nothing about the attempt, which goes on running, so a candidate who reports a
/// broken question can still answer the others. Staff read it in a queue and mark it resolved (<see cref="Resolve"/>), optionally
/// saying what was done.
/// </summary>
public sealed class IssueReport : AggregateRoot
{
    /// <summary>The longest description a candidate may write.</summary>
    public const int MaxMessageLength = 1000;

    /// <summary>The longest note staff may leave when resolving.</summary>
    public const int MaxNoteLength = 500;

    /// <summary>How many problems one attempt may report, so a stuck button or a rant cannot bury the queue.</summary>
    public const int MaxPerAttempt = 10;

    /// <summary>The attempt the candidate was sitting.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>The exam, kept here so staff can list reports without opening each attempt.</summary>
    public Guid ExamId { get; private set; }

    /// <summary>The candidate who reported; the signed-in user's id.</summary>
    public Guid CandidateId { get; private set; }

    /// <summary>The question on screen when they reported, when they reported it from one; null for a report about the page or anything else.</summary>
    public Guid? QuestionId { get; private set; }

    /// <summary>What kind of problem they said it was.</summary>
    public IssueCategory Category { get; private set; }

    /// <summary>What they wrote.</summary>
    public string Message { get; private set; }

    /// <summary>When they reported it.</summary>
    public DateTime ReportedAtUtc { get; private set; }

    /// <summary>Whether it is waiting or resolved.</summary>
    public IssueReportStatus Status { get; private set; }

    /// <summary>The staff user who resolved it; null while open.</summary>
    public Guid? ResolvedByUserId { get; private set; }

    /// <summary>When it was resolved.</summary>
    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>What staff said they did, if they said anything.</summary>
    public string? ResolutionNote { get; private set; }

    // For EF Core.
    private IssueReport() : base(Guid.Empty) => Message = null!;

    private IssueReport(Guid attemptId, Guid examId, Guid candidateId, Guid? questionId, IssueCategory category, string message, DateTime reportedAtUtc)
        : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        ExamId = examId;
        CandidateId = candidateId;
        QuestionId = questionId;
        Category = category;
        Message = message;
        ReportedAtUtc = reportedAtUtc;
        Status = IssueReportStatus.Open;
    }

    /// <summary>Records a new, open report.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="examId">The attempt's exam.</param>
    /// <param name="candidateId">The candidate who owns the attempt.</param>
    /// <param name="questionId">The question on screen, if the report is about one.</param>
    /// <param name="category">What kind of problem it is.</param>
    /// <param name="message">What is wrong, in the candidate's words; required, surrounding whitespace is removed.</param>
    /// <param name="reportedAtUtc">The current instant.</param>
    /// <exception cref="InvalidAttemptError">The category is not one of ours, or the message is missing or longer than <see cref="MaxMessageLength"/>.</exception>
    public static IssueReport Raise(
        Guid attemptId, Guid examId, Guid candidateId, Guid? questionId, IssueCategory category, string? message, DateTime reportedAtUtc)
    {
        if (!Enum.IsDefined(category))
            throw new InvalidAttemptError("Choose what kind of problem it is: a question, a technical problem, or something else.");

        var trimmed = message?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidAttemptError("Say what is wrong.");
        if (trimmed.Length > MaxMessageLength)
            throw new InvalidAttemptError($"The description must be at most {MaxMessageLength} characters.");

        var report = new IssueReport(attemptId, examId, candidateId, questionId, category, trimmed, reportedAtUtc);
        report.AddDomainEvent(new IssueReportedEvent(report.Id, attemptId, examId, candidateId, category));
        return report;
    }

    /// <summary>Marks the report resolved.</summary>
    /// <param name="resolvedByUserId">The staff user resolving it.</param>
    /// <param name="resolvedAtUtc">The current instant.</param>
    /// <param name="note">What was done, optional; surrounding whitespace is removed and a blank note is no note.</param>
    /// <exception cref="IssueReportNotOpenError">It was already resolved.</exception>
    /// <exception cref="InvalidAttemptError">The note is longer than <see cref="MaxNoteLength"/>.</exception>
    public void Resolve(Guid resolvedByUserId, DateTime resolvedAtUtc, string? note)
    {
        // A resolved report is a record of who dealt with it and when; resolving it again would rewrite that.
        if (Status != IssueReportStatus.Open)
            throw new IssueReportNotOpenError();

        var trimmed = note?.Trim();
        if (trimmed is { Length: > MaxNoteLength })
            throw new InvalidAttemptError($"The note must be at most {MaxNoteLength} characters.");

        Status = IssueReportStatus.Resolved;
        ResolvedByUserId = resolvedByUserId;
        ResolvedAtUtc = resolvedAtUtc;
        ResolutionNote = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        AddDomainEvent(new IssueResolvedEvent(Id, AttemptId, ExamId, CandidateId));
    }
}
