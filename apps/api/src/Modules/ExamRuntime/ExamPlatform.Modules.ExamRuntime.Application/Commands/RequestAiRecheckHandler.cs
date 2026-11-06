using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>
/// Has every typed answer that has no AI verdict checked by the AI evaluator in one request (FR-5, FR-29): for one attempt, from its paper, or for
/// every attempt of an exam. The way to send back the answers the platform marked by the accepted answers alone while the evaluator was off,
/// out of quota or failing, once it works.
/// </summary>
/// <remarks>
/// Only answers that are <see cref="AiCheckState.NotChecked"/> or <see cref="AiCheckState.GaveUp"/> are sent: one the evaluator already
/// judged, or one waiting in the queue, is left alone, so a second press costs nothing. Each answer is queued exactly as
/// <see cref="RequestAiCheckHandler"/> queues one, so the worker that does every other check makes it, paced by the provider's own limits, and
/// an attempt keeps its score until the verdicts are in. The exam-wide call reads the questions of each attempt that qualifies from the question
/// bank, one attempt after another; that is fine for the sizes this deployment sees, and is the part to move to a background job if an exam ever
/// has thousands of attempts.
/// </remarks>
public sealed class RequestAiRecheckHandler(
    StaffAttemptAccess access,
    IAttemptRepository attempts,
    IQuestionBank questionBank,
    ITextAnswerEvaluator evaluator,
    IExamRuntimeUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>Queues the typed answers of one attempt that have no verdict.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many answers were queued; none when every typed answer already has a verdict or is waiting for one.</returns>
    /// <exception cref="TextEvaluatorNotSetUpError">The evaluator has no key, or is switched off.</exception>
    /// <exception cref="AttemptNotFoundError">No such attempt at the exam.</exception>
    /// <exception cref="AttemptNotSubmittedError">The attempt is still open.</exception>
    /// <exception cref="InvalidAttemptError">The result was invalidated.</exception>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read.</exception>
    public async Task<AiRecheckResultDto> HandleForAttemptAsync(Guid examId, Guid attemptId, CancellationToken cancellationToken)
    {
        RequireEvaluator();
        var queued = await QueueAsync(examId, attemptId, strict: true, cancellationToken);
        return new AiRecheckResultDto(queued > 0 ? 1 : 0, queued);
    }

    /// <summary>
    /// Queues the typed answers with no verdict in every attempt of an exam that is over and still counts. An attempt still open, or one an
    /// administrator invalidated, is passed over.
    /// </summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many attempts and answers were queued; both zero when there is nothing to send, including for an exam with no attempts.</returns>
    /// <exception cref="TextEvaluatorNotSetUpError">The evaluator has no key, or is switched off.</exception>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read.</exception>
    public async Task<AiRecheckResultDto> HandleForExamAsync(Guid examId, CancellationToken cancellationToken)
    {
        RequireEvaluator();

        var attemptsQueued = 0;
        var answersQueued = 0;
        foreach (var attemptId in await attempts.ListOverIdsForExamAsync(examId, cancellationToken))
        {
            // One attempt at a time, each under its own lock and saved on its own: a verdict the worker is saving for one attempt waits only for
            // that attempt, and a failure part way leaves every attempt already queued queued, which asking again simply skips.
            var queued = await QueueAsync(examId, attemptId, strict: false, cancellationToken);
            if (queued == 0)
                continue;

            attemptsQueued++;
            answersQueued += queued;
        }

        return new AiRecheckResultDto(attemptsQueued, answersQueued);
    }

    private void RequireEvaluator()
    {
        if (!evaluator.IsEnabled)
            throw new TextEvaluatorNotSetUpError();
    }

    // strict: asked about this one attempt, so an attempt that cannot be rechecked is an error to say; otherwise it is only passed over.
    private async Task<int> QueueAsync(Guid examId, Guid attemptId, bool strict, CancellationToken cancellationToken)
    {
        // Exclusive, and taken before the attempt is loaded, so a verdict being saved by the worker is either in what is read or waits for this.
        await using var hold = await unitOfWork.LockAttemptAsync(attemptId, exclusive: true, cancellationToken);
        var (attempt, exam) = await access.LoadAsync(examId, attemptId, cancellationToken);

        // The status can have changed between listing the attempts and locking this one.
        if (attempt.Status == AttemptStatus.InProgress)
            return strict ? throw new AttemptNotSubmittedError() : 0;
        if (attempt.IsInvalidated)
            return strict ? throw new InvalidAttemptError("This result was invalidated, so there is no score for a check to change.") : 0;

        // The paper as this attempt sat it (its drawn questions, if it drew), under the question versions it was pinned to.
        var questionIds = exam.Sections.SelectMany(s => s.QuestionIds).ToList();
        var questions = await questionBank.ReadAsync(attempt, questionIds, cancellationToken);
        var toAsk = TypedAnswerChecks.QuestionsWithoutVerdict(attempt, questions);
        if (toAsk.Count == 0)
            return 0;

        var nowUtc = clock.UtcNow;
        foreach (var questionId in toAsk)
            attempt.RequestAiCheck(questionId, RequestAiCheckHandler.Reason, nowUtc);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await hold.CompleteAsync(cancellationToken);
        return toAsk.Count;
    }
}
