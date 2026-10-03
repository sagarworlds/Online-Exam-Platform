using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>Saves the option, or the set of options, a candidate chose for one question, replacing an earlier choice (FR-18).</summary>
public sealed class SaveAnswerHandler(
    AttemptAccess access,
    IQuestionBank questionBank,
    IExamRuntimeUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>Checks the answer belongs to the exam and records it. For questions that take one answer.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="questionId">The question answered.</param>
    /// <param name="optionId">The option chosen.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidAnswerError">The question is not in the exam, or the option is not one of its options.</exception>
    public Task HandleAsync(Guid attemptId, Guid candidateId, Guid questionId, Guid optionId, CancellationToken cancellationToken) =>
        HandleAsync(attemptId, candidateId, questionId, [optionId], cancellationToken);

    /// <summary>Checks the answer belongs to the exam and records it, replacing any earlier answer to the question.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="questionId">The question answered.</param>
    /// <param name="optionIds">The options chosen: exactly one for a single-answer question, one or more for a multiple-answer one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted, or its time has just run out and it was closed.</exception>
    /// <exception cref="InvalidAnswerError">
    /// The question is not in the exam, no option was chosen, an option is not one of the question's options, or several were chosen
    /// for a question that takes one answer.
    /// </exception>
    /// <exception cref="SectionLockedError">The exam locks sections and the question is in a section the candidate is not in.</exception>
    /// <exception cref="ConcurrencyConflictError">The same answer was saved twice at the same moment.</exception>
    public async Task HandleAsync(
        Guid attemptId, Guid candidateId, Guid questionId, IReadOnlyCollection<Guid> optionIds, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        // Every id comes from the client, so none is trusted: the question must be in this exam and each option on that question,
        // or an answer could be filed against anything.
        if (!exam.Includes(questionId))
            throw new InvalidAnswerError();

        var chosen = optionIds.Distinct().ToList();
        if (chosen.Count == 0)
            throw new InvalidAnswerError();

        var question = (await questionBank.GetAsync([questionId], cancellationToken)).FirstOrDefault()
            ?? throw new ExamContentUnavailableError();
        var known = question.Options.Select(o => o.Id).ToHashSet();
        if (!chosen.All(known.Contains))
            throw new InvalidAnswerError();

        // A single-answer question takes exactly one option: several would be a client that does not know the question's shape.
        if (!question.AllowsMultiple && chosen.Count != 1)
            throw new InvalidAnswerError();

        SectionLock.EnsureQuestionReachable(exam, attempt, questionId);
        attempt.RecordAnswer(questionId, chosen, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
