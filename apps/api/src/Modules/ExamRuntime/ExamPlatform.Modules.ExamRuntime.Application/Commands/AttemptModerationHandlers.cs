using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>
/// Finds the attempt an administrator is acting on (FR-29). Unlike the candidate's own access it is named by exam and attempt, so an
/// attempt id that belongs to another exam answers like a missing one, and it is closed first if its time has run out, so no action is
/// ever taken on an attempt that should already be over.
/// </summary>
public sealed class StaffAttemptAccess(IAttemptRepository attempts, IExamCatalog catalog, AttemptAccess access)
{
    /// <summary>Loads the attempt with its exam, closing it first if time has run out.</summary>
    /// <param name="examId">The exam named in the route.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is an attempt at another exam.</exception>
    /// <exception cref="ExamContentUnavailableError">The attempt's exam can no longer be read.</exception>
    public async Task<(Attempt Attempt, ExamSnapshot Exam)> LoadAsync(Guid examId, Guid attemptId, CancellationToken cancellationToken)
    {
        var attempt = await attempts.GetByIdAsync(attemptId, cancellationToken);
        if (attempt is null || attempt.ExamId != examId)
            throw new AttemptNotFoundError();

        var exam = await catalog.FindAsync(examId, cancellationToken) ?? throw new ExamContentUnavailableError();
        exam = exam.For(attempt);
        await access.CloseIfExpiredAsync(attempt, exam, cancellationToken);
        return (attempt, exam);
    }
}

/// <summary>Sends a candidate a warning during their attempt (FR-29).</summary>
public sealed class WarnAttemptHandler(StaffAttemptAccess access, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Records the warning; the candidate's page shows it within seconds.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="staffUserId">The administrator, from their token and never from the request.</param>
    /// <param name="message">What to tell the candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt at the exam.</exception>
    /// <exception cref="InvalidAttemptError">The message is empty or too long.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already over.</exception>
    public async Task<AttemptSummaryDto> HandleAsync(Guid examId, Guid attemptId, Guid staffUserId, string? message, CancellationToken cancellationToken)
    {
        var (attempt, _) = await access.LoadAsync(examId, attemptId, cancellationToken);
        attempt.Warn(message, staffUserId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ExamCandidateRows.StaffSummary(attempt);
    }
}

/// <summary>Pauses a candidate's attempt: they cannot answer, and the clock stops (FR-29).</summary>
public sealed class PauseAttemptHandler(StaffAttemptAccess access, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Pauses the attempt.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt at the exam.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already over.</exception>
    /// <exception cref="AttemptAlreadyPausedError">The attempt is already paused.</exception>
    /// <exception cref="ConcurrencyConflictError">The candidate submitted at the same moment.</exception>
    public async Task<AttemptSummaryDto> HandleAsync(Guid examId, Guid attemptId, CancellationToken cancellationToken)
    {
        var (attempt, _) = await access.LoadAsync(examId, attemptId, cancellationToken);
        attempt.Pause(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ExamCandidateRows.StaffSummary(attempt);
    }
}

/// <summary>Resumes a paused attempt, giving the candidate back the time they lost (FR-29).</summary>
public sealed class ResumeAttemptHandler(StaffAttemptAccess access, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Resumes the attempt; its deadline moves later by the time it was paused.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt at the exam.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already over.</exception>
    /// <exception cref="AttemptNotPausedError">The attempt is not paused.</exception>
    /// <exception cref="ConcurrencyConflictError">The attempt was changed at the same moment.</exception>
    public async Task<AttemptSummaryDto> HandleAsync(Guid examId, Guid attemptId, CancellationToken cancellationToken)
    {
        var (attempt, _) = await access.LoadAsync(examId, attemptId, cancellationToken);
        attempt.Resume(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ExamCandidateRows.StaffSummary(attempt);
    }
}

/// <summary>Ends a candidate's attempt early, scored with what they had saved (FR-29).</summary>
public sealed class TerminateAttemptHandler(StaffAttemptAccess access, AttemptCloser closer)
{
    /// <summary>Scores and closes the attempt.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="staffUserId">The administrator, from their token.</param>
    /// <param name="reason">Why; the candidate is shown it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt at the exam.</exception>
    /// <exception cref="InvalidAttemptError">The reason is empty or too long.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already over.</exception>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read, so it cannot be marked.</exception>
    /// <exception cref="ConcurrencyConflictError">The candidate submitted at the same moment.</exception>
    public async Task<AttemptSummaryDto> HandleAsync(Guid examId, Guid attemptId, Guid staffUserId, string? reason, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadAsync(examId, attemptId, cancellationToken);
        await closer.TerminateAsync(attempt, exam, staffUserId, reason, cancellationToken);
        return ExamCandidateRows.StaffSummary(attempt);
    }
}

/// <summary>Invalidates a finished attempt's result so it no longer counts (FR-29).</summary>
public sealed class InvalidateAttemptHandler(StaffAttemptAccess access, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Marks the result invalid; the candidate is told why instead of being shown a score.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="staffUserId">The administrator, from their token.</param>
    /// <param name="reason">Why; the candidate is shown it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt at the exam.</exception>
    /// <exception cref="InvalidAttemptError">The reason is empty or too long.</exception>
    /// <exception cref="AttemptNotSubmittedError">The attempt is still open: end it first.</exception>
    /// <exception cref="AttemptAlreadyInvalidatedError">The result was already invalidated.</exception>
    public async Task<AttemptSummaryDto> HandleAsync(Guid examId, Guid attemptId, Guid staffUserId, string? reason, CancellationToken cancellationToken)
    {
        var (attempt, _) = await access.LoadAsync(examId, attemptId, cancellationToken);
        attempt.Invalidate(staffUserId, reason, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ExamCandidateRows.StaffSummary(attempt);
    }
}
