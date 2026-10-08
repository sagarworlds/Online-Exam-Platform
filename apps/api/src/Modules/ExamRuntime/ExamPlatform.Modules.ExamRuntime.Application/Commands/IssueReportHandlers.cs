using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>
/// Lets a candidate report a problem from inside the exam they are sitting (FR-42). Reporting changes nothing about the attempt: it
/// goes on, the clock keeps running, and the report waits in the staff queue. A paused attempt may report too, since a candidate
/// who was stopped is the one most likely to have something to say.
/// </summary>
public sealed class ReportIssueHandler(AttemptAccess access, IIssueReportRepository reports, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Records the report, to be read by staff.</summary>
    /// <param name="attemptId">The attempt they are sitting.</param>
    /// <param name="candidateId">The signed-in candidate, taken from their token.</param>
    /// <param name="category">What kind of problem it is.</param>
    /// <param name="questionId">The question on screen, when the report is about one.</param>
    /// <param name="message">What is wrong; required, at most 1000 characters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report as recorded.</returns>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted, so it is no longer "in the exam".</exception>
    /// <exception cref="QuestionNotInAttemptError">A question was named that is not part of this attempt.</exception>
    /// <exception cref="TooManyIssueReportsError">The attempt already carries as many reports as one may.</exception>
    /// <exception cref="InvalidAttemptError">The category is not one of ours, or the message is missing or too long.</exception>
    public async Task<MyIssueReportDto> HandleAsync(
        Guid attemptId, Guid candidateId, IssueCategory category, Guid? questionId, string? message, CancellationToken cancellationToken)
    {
        // Ownership first: someone else's attempt answers exactly like a missing one, before anything about it is revealed.
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        if (attempt.Status != AttemptStatus.InProgress)
            throw new AttemptNotInProgressError();

        // The exam is already the one this attempt sits, drawn paper included.
        if (questionId is { } question && !exam.Sections.SelectMany(s => s.QuestionIds).Contains(question))
            throw new QuestionNotInAttemptError();

        // A soft limit against floods, not a security boundary, so two reports sent at the same instant may both pass it.
        if (await reports.CountForAttemptAsync(attemptId, cancellationToken) >= IssueReport.MaxPerAttempt)
            throw new TooManyIssueReportsError();

        var report = IssueReport.Raise(attemptId, attempt.ExamId, candidateId, questionId, category, message, clock.UtcNow);
        reports.Add(report);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return IssueReportDtoFactory.ForCandidate(report);
    }
}

/// <summary>Lists the problems candidates have reported, for staff to work through.</summary>
public sealed class ListIssueReportsHandler(IIssueReportRepository reports, IssueReportDtoFactory dtos)
{
    /// <summary>How many reports one listing returns at most.</summary>
    public const int PageSize = 200;

    /// <summary>Returns the reports with the given status, oldest first.</summary>
    /// <param name="status">Which reports to list; open ones when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<IssueReportDto>> HandleAsync(IssueReportStatus? status, CancellationToken cancellationToken) =>
        await dtos.CreateAsync(await reports.ListAsync(status ?? IssueReportStatus.Open, PageSize, cancellationToken), cancellationToken);
}

/// <summary>Lets staff mark a reported problem as dealt with.</summary>
public sealed class ResolveIssueReportHandler(IIssueReportRepository reports, IExamRuntimeUnitOfWork unitOfWork, IssueReportDtoFactory dtos, Clock clock)
{
    /// <summary>Marks the report resolved and saves.</summary>
    /// <param name="issueReportId">The report.</param>
    /// <param name="resolvedByUserId">The signed-in staff user, taken from their token.</param>
    /// <param name="note">What was done; optional, at most 500 characters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="IssueReportNotFoundError">No report has that id.</exception>
    /// <exception cref="IssueReportNotOpenError">The report was already resolved.</exception>
    /// <exception cref="InvalidAttemptError">The note is too long.</exception>
    public async Task<IssueReportDto> HandleAsync(Guid issueReportId, Guid resolvedByUserId, string? note, CancellationToken cancellationToken)
    {
        var report = await reports.GetByIdAsync(issueReportId, cancellationToken) ?? throw new IssueReportNotFoundError();

        report.Resolve(resolvedByUserId, clock.UtcNow, note);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (await dtos.CreateAsync([report], cancellationToken))[0];
    }
}
