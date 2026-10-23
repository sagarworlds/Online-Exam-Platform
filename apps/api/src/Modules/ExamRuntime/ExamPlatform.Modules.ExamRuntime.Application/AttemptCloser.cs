using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Scores and closes an attempt. Used both when a candidate submits and when somebody touches an attempt
/// whose deadline has passed: with no background worker in this first cut, an abandoned attempt is closed the
/// next time anyone looks at it, at its deadline, with whatever answers were saved.
/// </summary>
/// <remarks>
/// When the AI evaluator is set up and the attempt holds a typed answer that matches none of the accepted answers word for word, the attempt
/// is closed without a score and waits in <see cref="AttemptStatus.Grading"/> until the evaluator has judged those answers; a background
/// worker (<see cref="TypedAnswerGrading"/>) gives the score then. With no evaluator, or nothing for it to judge, the attempt is scored here
/// at once, exactly as it always was.
/// </remarks>
public sealed class AttemptCloser(IQuestionBank questionBank, IExamRuntimeUnitOfWork unitOfWork, Clock clock, ITextAnswerEvaluator? evaluator = null)
{
    /// <summary>Marks the attempt, ends it and saves it.</summary>
    /// <param name="attempt">The open attempt to close.</param>
    /// <param name="exam">The exam it is an attempt at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read, so it cannot be marked.</exception>
    public Task CloseAsync(Attempt attempt, ExamSnapshot exam, CancellationToken cancellationToken) =>
        CloseAsync(attempt, exam, endedByViolations: false, cancellationToken);

    /// <summary>Marks the attempt, ends it because the candidate left the exam page too often (FR-22), and saves it.</summary>
    /// <param name="attempt">The open attempt to close.</param>
    /// <param name="exam">The exam it is an attempt at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read, so it cannot be marked.</exception>
    public Task CloseForViolationsAsync(Attempt attempt, ExamSnapshot exam, CancellationToken cancellationToken) =>
        CloseAsync(attempt, exam, endedByViolations: true, cancellationToken);

    /// <summary>Marks the attempt, ends it on an administrator's decision (FR-29), and saves it.</summary>
    /// <param name="attempt">The open attempt to end.</param>
    /// <param name="exam">The exam it is an attempt at.</param>
    /// <param name="byUserId">The administrator, from their token.</param>
    /// <param name="reason">Why; the candidate is shown it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidAttemptError">The reason is empty or too long.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read, so it cannot be marked.</exception>
    public async Task TerminateAsync(Attempt attempt, ExamSnapshot exam, Guid byUserId, string? reason, CancellationToken cancellationToken)
    {
        var questionIds = exam.Sections.SelectMany(s => s.QuestionIds).ToList();
        var questions = await questionBank.ReadAsync(attempt, questionIds, cancellationToken);

        // Worked out even when the attempt is to wait for the evaluator, so an exam whose questions cannot be read still fails here, as before.
        var result = AttemptScorer.Score(exam, questions, attempt.Answers.ToList());
        var toCheck = ToCheck(attempt, questions);
        if (toCheck.Count > 0)
            attempt.TerminateForGrading(clock.UtcNow, byUserId, reason, toCheck);
        else
            attempt.Terminate(clock.UtcNow, result.Score, result.MaxScore, byUserId, reason);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task CloseAsync(Attempt attempt, ExamSnapshot exam, bool endedByViolations, CancellationToken cancellationToken)
    {
        var questionIds = exam.Sections.SelectMany(s => s.QuestionIds).ToList();
        var questions = await questionBank.ReadAsync(attempt, questionIds, cancellationToken);

        var result = AttemptScorer.Score(exam, questions, attempt.Answers.ToList());
        var toCheck = ToCheck(attempt, questions);
        if (toCheck.Count > 0)
            attempt.SubmitForGrading(clock.UtcNow, toCheck, endedByViolations);
        else
            attempt.Submit(clock.UtcNow, result.Score, result.MaxScore, endedByViolations);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // The typed answers the evaluator is to judge before the attempt can be scored; none when no evaluator is set up.
    private IReadOnlyList<Guid> ToCheck(Attempt attempt, IReadOnlyDictionary<Guid, QuestionSnapshot> questions) =>
        evaluator is { IsEnabled: true } ? TypedAnswerChecks.QuestionsToCheck(attempt, questions) : [];
}
