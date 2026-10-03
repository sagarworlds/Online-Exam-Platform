using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>Saves the option a candidate chose for one question, replacing an earlier choice (FR-18).</summary>
public sealed class SaveAnswerHandler(
    AttemptAccess access,
    IQuestionBank questionBank,
    IExamRuntimeUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>Checks the answer belongs to the exam and records it.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="questionId">The question answered.</param>
    /// <param name="optionId">The option chosen.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted, or its time has just run out and it was closed.</exception>
    /// <exception cref="InvalidAnswerError">The question is not in the exam, or the option is not one of its options.</exception>
    /// <exception cref="SectionLockedError">The exam locks sections and the question is in a section the candidate is not in.</exception>
    /// <exception cref="ConcurrencyConflictError">The same answer was saved twice at the same moment.</exception>
    public async Task HandleAsync(Guid attemptId, Guid candidateId, Guid questionId, Guid optionId, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        // Both ids come from the client, so neither is trusted: the question must be in this exam and the
        // option on that question, or an answer could be filed against anything.
        if (!exam.Includes(questionId))
            throw new InvalidAnswerError();

        var question = (await questionBank.GetAsync([questionId], cancellationToken)).FirstOrDefault()
            ?? throw new ExamContentUnavailableError();
        if (question.Options.All(o => o.Id != optionId))
            throw new InvalidAnswerError();

        SectionLock.EnsureQuestionReachable(exam, attempt, questionId);
        attempt.RecordAnswer(questionId, optionId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
