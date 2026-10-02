using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>Takes back the option a candidate chose for one question, so it counts as unanswered again (FR-18, "clear response").</summary>
public sealed class ClearAnswerHandler(AttemptAccess access, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Checks the question belongs to the exam and removes the saved answer, if there is one.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="questionId">The question to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted, or its time has just run out and it was closed.</exception>
    /// <exception cref="QuestionNotInAttemptError">The question is not in the exam.</exception>
    /// <exception cref="ConcurrencyConflictError">Another request changed the same attempt at the same moment.</exception>
    public async Task HandleAsync(Guid attemptId, Guid candidateId, Guid questionId, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        if (!exam.Includes(questionId))
            throw new QuestionNotInAttemptError();

        attempt.ClearAnswer(questionId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
