using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>
/// Saves what a candidate answered for one question (FR-18): the option, or the set of options, they chose, or, for a text question, the
/// answer they typed. The two kinds of question are checked differently, but both go through the same steps, so a save is refused or
/// stored the same way whatever the question is.
/// </summary>
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
    /// The question is not in the exam, is a text question (which takes a typed answer), no option was chosen, an option is not one of the
    /// question's options, or several were chosen for a question that takes one answer.
    /// </exception>
    /// <exception cref="SectionLockedError">The exam locks sections and the question is in a section the candidate is not in.</exception>
    /// <exception cref="ConcurrencyConflictError">The same answer was saved twice at the same moment.</exception>
    public Task HandleAsync(
        Guid attemptId, Guid candidateId, Guid questionId, IReadOnlyCollection<Guid> optionIds, CancellationToken cancellationToken) =>
        RecordAsync(attemptId, candidateId, questionId, (attempt, exam, question) =>
        {
            var chosen = optionIds.Distinct().ToList();

            // Every id comes from the client, so none is trusted: each option must be one of this question's.
            if (chosen.Count == 0 || question.IsTextAnswer)
                throw new InvalidAnswerError();

            var known = question.Options.Select(o => o.Id).ToHashSet();
            if (!chosen.All(known.Contains))
                throw new InvalidAnswerError();

            // A single-answer question takes exactly one option: several would be a client that does not know the question's shape.
            if (!question.AllowsMultiple && chosen.Count != 1)
                throw new InvalidAnswerError();

            SectionLock.EnsureQuestionReachable(exam, attempt, questionId);
            attempt.RecordAnswer(questionId, chosen, clock.UtcNow);
        }, cancellationToken);

    /// <summary>
    /// Checks the question is in the exam and is a text question, then records what the candidate typed for it, replacing any earlier answer.
    /// A blank answer is refused: to take an answer back, the candidate clears it.
    /// </summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="questionId">The question answered.</param>
    /// <param name="text">What the candidate typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted, or its time has just run out and it was closed.</exception>
    /// <exception cref="InvalidAnswerError">The question is not in the exam, is not a text question, or the text is blank or too long.</exception>
    /// <exception cref="SectionLockedError">The exam locks sections and the question is in a section the candidate is not in.</exception>
    public Task HandleTextAsync(Guid attemptId, Guid candidateId, Guid questionId, string? text, CancellationToken cancellationToken) =>
        RecordAsync(attemptId, candidateId, questionId, (attempt, exam, question) =>
        {
            if (!question.IsTextAnswer)
                throw new InvalidAnswerError();

            SectionLock.EnsureQuestionReachable(exam, attempt, questionId);
            attempt.RecordTextAnswer(questionId, text, clock.UtcNow);
        }, cancellationToken);

    // The steps both kinds of answer share: lock the attempt, load it, check the question is in its exam, let the caller check and record
    // the answer, then save. The lock is shared with other answers but not with a submit: see IExamRuntimeUnitOfWork.LockAttemptAsync.
    private async Task RecordAsync(
        Guid attemptId, Guid candidateId, Guid questionId, Action<Attempt, ExamSnapshot, QuestionSnapshot> record, CancellationToken cancellationToken)
    {
        await using var hold = await unitOfWork.LockAttemptAsync(attemptId, exclusive: false, cancellationToken);
        try
        {
            var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

            // The question must be in this exam, or an answer could be filed against anything.
            if (!exam.Includes(questionId))
                throw new InvalidAnswerError();

            var question = (await questionBank.ReadAsync(attempt, [questionId], cancellationToken)).GetValueOrDefault(questionId)
                ?? throw new ExamContentUnavailableError();

            record(attempt, exam, question);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await hold.CompleteAsync(cancellationToken);
        }
        catch (AttemptNotInProgressError)
        {
            // Time ran out and loading the attempt closed it: keep that, so the closing is not undone along with this refused answer.
            await hold.CompleteAsync(cancellationToken);
            throw;
        }
    }
}
