using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>
/// Has a candidate's typed answer checked by the AI evaluator on staff's request (FR-5, FR-29), from the paper: the way to judge an answer the
/// platform marked by the accepted answers alone (the evaluator was off, or gave up), or to ask again for one it judged.
/// </summary>
/// <remarks>
/// The check is queued, not made in the request: it can take a minute, and the worker that does every other check does it, with the same retries.
/// The attempt keeps its score until the verdict is in. Then the score is revised if the verdict moves it, as a revision the candidate sees in
/// their review, and an attempt still in grading is scored.
/// </remarks>
public sealed class RequestAiCheckHandler(
    StaffAttemptAccess access, IQuestionBank questionBank, ITextAnswerEvaluator evaluator, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>The reason a candidate is shown if the check changes their score.</summary>
    public const string Reason = "An organiser had your typed answer to a question checked again.";

    /// <summary>Queues the check. Asking about an answer that is already queued changes nothing.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="questionId">The question whose typed answer is to be checked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attempt as staff see it.</returns>
    /// <exception cref="TextEvaluatorNotSetUpError">The evaluator has no key, or is switched off.</exception>
    /// <exception cref="AttemptNotFoundError">No such attempt at the exam.</exception>
    /// <exception cref="AttemptNotSubmittedError">The attempt is still open.</exception>
    /// <exception cref="QuestionNotInAttemptError">The question is not on this attempt's paper.</exception>
    /// <exception cref="InvalidAttemptError">The question is not a text question, the candidate typed no answer, what they typed matches an accepted answer under the question's matching rule, or the result was invalidated.</exception>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read.</exception>
    public async Task<AttemptSummaryDto> HandleAsync(Guid examId, Guid attemptId, Guid questionId, CancellationToken cancellationToken)
    {
        if (!evaluator.IsEnabled)
            throw new TextEvaluatorNotSetUpError();

        // Exclusive, and taken before the attempt is loaded, so a verdict being saved by the worker is either in what is read or waits for this.
        await using var hold = await unitOfWork.LockAttemptAsync(attemptId, exclusive: true, cancellationToken);
        var (attempt, exam) = await access.LoadAsync(examId, attemptId, cancellationToken);
        if (attempt.Status == AttemptStatus.InProgress)
            throw new AttemptNotSubmittedError();
        if (!exam.Includes(questionId))
            throw new QuestionNotInAttemptError();

        var questions = await questionBank.ReadAsync(attempt, [questionId], cancellationToken);
        var question = questions.GetValueOrDefault(questionId) ?? throw new ExamContentUnavailableError();
        if (!question.IsTextAnswer)
            throw new InvalidAttemptError("Only an answer the candidate typed can be checked by the AI evaluator.");

        var answer = attempt.Answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (answer is null || string.IsNullOrWhiteSpace(answer.AnswerText))
            throw new InvalidAttemptError("The candidate typed no answer to this question.");
        if (TypedAnswer.Matches(answer.AnswerText, question.AcceptedAnswers ?? [], question.MatchRule))
            throw new InvalidAttemptError("What the candidate typed matches an accepted answer, so it needs no check.");

        attempt.RequestAiCheck(questionId, Reason, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await hold.CompleteAsync(cancellationToken);

        return ExamCandidateRows.StaffSummary(attempt);
    }
}
