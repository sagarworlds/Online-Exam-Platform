using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>Marks a question for review, or takes the mark off, so a candidate can find their way back to it (FR-18).</summary>
public sealed class MarkQuestionHandler(AttemptAccess access, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Checks the question belongs to the exam and sets whether it is marked.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="questionId">The question.</param>
    /// <param name="marked">True to mark it, false to take the mark off.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted, or its time has just run out and it was closed.</exception>
    /// <exception cref="QuestionNotInAttemptError">The question is not in the exam.</exception>
    /// <exception cref="SectionLockedError">The exam locks sections and the question is in a section the candidate is not in.</exception>
    /// <exception cref="ConcurrencyConflictError">The same question was marked twice at the same moment.</exception>
    public async Task HandleAsync(Guid attemptId, Guid candidateId, Guid questionId, bool marked, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        if (!exam.Includes(questionId))
            throw new QuestionNotInAttemptError();

        SectionLock.EnsureQuestionReachable(exam, attempt, questionId);
        attempt.SetMarked(questionId, marked, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
