using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>
/// Records how long a candidate has spent on a question, as the page's running total (FR-36, time per question). It is a measure, not an
/// answer, so it takes no row lock on the attempt and is never scored; a report that arrives after the attempt has ended is refused, and the
/// page keeps no reason to send it again.
/// </summary>
public sealed class RecordQuestionTimeHandler(AttemptAccess access, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Checks the question belongs to the exam and raises the time the candidate has spent on it.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="questionId">The question the time was spent on.</param>
    /// <param name="seconds">The running total for the question, in seconds, as the page has it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted, or its time has just run out and it was closed.</exception>
    /// <exception cref="AttemptPausedError">An administrator has paused the attempt.</exception>
    /// <exception cref="QuestionNotInAttemptError">The question is not in the exam.</exception>
    /// <exception cref="SectionLockedError">The exam locks sections and the question is in a section the candidate is not in.</exception>
    public async Task HandleAsync(Guid attemptId, Guid candidateId, Guid questionId, int seconds, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        if (!exam.Includes(questionId))
            throw new QuestionNotInAttemptError();

        SectionLock.EnsureQuestionReachable(exam, attempt, questionId);
        attempt.RecordQuestionTime(questionId, seconds, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
