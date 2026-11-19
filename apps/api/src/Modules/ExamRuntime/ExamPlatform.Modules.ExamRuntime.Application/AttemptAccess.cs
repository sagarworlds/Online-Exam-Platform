using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Finds the attempt a candidate is acting on and makes sure it is current: an open attempt whose deadline has
/// passed is closed here, so no handler ever works on an attempt that should already be over.
/// </summary>
public sealed class AttemptAccess(
    IAttemptRepository attempts, IExamCatalog catalog, AttemptCloser closer, Clock clock, IProctorPolicy proctorPolicy, IAnswerAttemptLoader? answerLoader = null)
{
    /// <summary>
    /// As <see cref="LoadOwnedAsync"/>, for a write to one question's answer: only that question's saved answer and sequence number are loaded
    /// with the attempt, which is all such a write reads or changes. A save is the commonest call a candidate makes, and loading the whole
    /// attempt for it costs ten queries.
    /// </summary>
    /// <remarks>
    /// Without a loader (a host or test that has none) this is <see cref="LoadOwnedAsync"/>. An attempt whose time has run out is read in full
    /// before it is closed, because closing scores every answer.
    /// </remarks>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate; an attempt that belongs to someone else is reported as not found.</param>
    /// <param name="questionId">The question being answered or cleared.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it belongs to another candidate.</exception>
    /// <exception cref="ExamContentUnavailableError">The attempt's exam can no longer be read.</exception>
    public async Task<(Attempt Attempt, ExamSnapshot Exam)> LoadOwnedForAnswerAsync(
        Guid attemptId, Guid candidateId, Guid questionId, CancellationToken cancellationToken)
    {
        if (answerLoader is null)
            return await LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        var attempt = await answerLoader.LoadForAnswerAsync(attemptId, questionId, cancellationToken);

        // Someone else's attempt answers exactly like a missing one, so ids cannot be probed.
        if (attempt is null || attempt.CandidateId != candidateId)
            throw new AttemptNotFoundError();

        var exam = await catalog.FindAsync(attempt.ExamId, cancellationToken)
            ?? throw new ExamContentUnavailableError();
        exam = exam.For(attempt, proctorPolicy);

        if (attempt.Status == AttemptStatus.InProgress && attempt.IsExpired(clock.UtcNow))
        {
            // Closing scores every answer, so read the attempt in full: it is the object already tracked, now with all its collections.
            attempt = await attempts.GetByIdAsync(attemptId, cancellationToken) ?? throw new AttemptNotFoundError();
            await closer.CloseAsync(attempt, exam, cancellationToken);
        }

        return (attempt, exam);
    }

    /// <summary>Loads one of the candidate's own attempts together with its exam, closing it first if time has run out.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate; an attempt that belongs to someone else is reported as not found.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it belongs to another candidate.</exception>
    /// <exception cref="ExamContentUnavailableError">The attempt's exam can no longer be read.</exception>
    public async Task<(Attempt Attempt, ExamSnapshot Exam)> LoadOwnedAsync(Guid attemptId, Guid candidateId, CancellationToken cancellationToken)
    {
        var attempt = await attempts.GetByIdAsync(attemptId, cancellationToken);

        // Someone else's attempt answers exactly like a missing one, so ids cannot be probed.
        if (attempt is null || attempt.CandidateId != candidateId)
            throw new AttemptNotFoundError();

        var exam = await catalog.FindAsync(attempt.ExamId, cancellationToken)
            ?? throw new ExamContentUnavailableError();

        // The exam as this attempt sees it: its drawn paper, if the exam draws one.
        exam = exam.For(attempt, proctorPolicy);
        await CloseIfExpiredAsync(attempt, exam, cancellationToken);
        return (attempt, exam);
    }

    /// <summary>Closes the attempt if it is still open and its deadline has passed; otherwise does nothing.</summary>
    /// <param name="attempt">The attempt.</param>
    /// <param name="exam">Its exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CloseIfExpiredAsync(Attempt attempt, ExamSnapshot exam, CancellationToken cancellationToken)
    {
        if (attempt.Status == AttemptStatus.InProgress && attempt.IsExpired(clock.UtcNow))
            await closer.CloseAsync(attempt, exam, cancellationToken);
    }
}
