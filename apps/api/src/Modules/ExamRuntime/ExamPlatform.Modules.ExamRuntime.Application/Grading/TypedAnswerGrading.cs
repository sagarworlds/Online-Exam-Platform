using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Application.Grading;

/// <summary>
/// The database side of judging typed answers with the AI evaluator: taking an answer off the queue, saying what to ask about it, and saving
/// the verdict, which also gives the attempt its score once its last typed answer has one. The ask itself, which can take minutes and must not
/// hold a database transaction open, is made by the caller between <see cref="PrepareAsync"/> and <see cref="RecordAsync"/>, each in a scope
/// of its own so that what is saved is read fresh from the database and never from a copy another worker has since changed.
/// </summary>
/// <remarks>
/// Nothing here writes the question, the key's answers or what the candidate typed anywhere but the database: a failure is recorded as a few
/// words, never as the content.
/// </remarks>
public sealed class TypedAnswerGrading(
    IAttemptRepository attempts,
    IExamCatalog catalog,
    IQuestionBank questionBank,
    IExamRuntimeUnitOfWork unitOfWork,
    IProctorPolicy proctorPolicy,
    Clock clock,
    IOptions<AiGradingOptions> options)
{
    /// <summary>Takes up to <paramref name="limit"/> typed answers off the queue and holds them for this worker.</summary>
    /// <param name="limit">The most to take.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The answers taken; none when none is due.</returns>
    public Task<IReadOnlyList<GradingClaim>> ClaimAsync(int limit, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        return attempts.ClaimAnswersAwaitingEvaluationAsync(now, now.AddMinutes(options.Value.LeaseMinutes), limit, cancellationToken);
    }

    /// <summary>Works out what to ask the evaluator about a claimed answer: the question in plain words, the answers it accepts, and what the candidate typed.</summary>
    /// <param name="claim">The answer taken off the queue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request, or <see langword="null"/> when the answer is no longer waiting for a verdict.</returns>
    /// <exception cref="ExamContentUnavailableError">The question can no longer be read, or has no accepted answer to judge against.</exception>
    public async Task<TextEvaluationRequest?> PrepareAsync(GradingClaim claim, CancellationToken cancellationToken)
    {
        var attempt = await attempts.GetByIdAsync(claim.AttemptId, cancellationToken);
        var answer = attempt is null ? null : StillWaiting(attempt, claim);
        if (attempt is null || answer is null || string.IsNullOrWhiteSpace(answer.AnswerText))
            return null;

        // At the version the candidate sat, so the answer is judged against the key it was set.
        var questions = await questionBank.ReadAsync(attempt, [claim.QuestionId], cancellationToken);
        if (!questions.TryGetValue(claim.QuestionId, out var question) || !question.IsTextAnswer || question.AcceptedAnswers is not { Count: > 0 } accepted)
            throw new ExamContentUnavailableError();

        return new TextEvaluationRequest(QuestionPlainText.FromHtml(question.Text), accepted, answer.AnswerText);
    }

    /// <summary>
    /// Saves the evaluator's verdict, and when it was the last one the attempt was waiting for, scores the attempt. The attempt is locked while
    /// this runs, so two verdicts for the same attempt saved together cannot each think the other is still to come.
    /// </summary>
    /// <param name="claim">The answer taken off the queue.</param>
    /// <param name="evaluation">What the evaluator decided.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ConcurrencyConflictError">Another request changed the attempt at the same moment; the caller tries again in a fresh scope.</exception>
    /// <exception cref="ExamContentUnavailableError">The attempt's exam or questions can no longer be read, so it cannot be scored.</exception>
    public async Task RecordAsync(GradingClaim claim, TextEvaluation evaluation, CancellationToken cancellationToken)
    {
        await using var hold = await unitOfWork.LockAttemptAsync(claim.AttemptId, exclusive: true, cancellationToken);
        var attempt = await attempts.GetByIdAsync(claim.AttemptId, cancellationToken);

        // Queued again meanwhile, after a key correction: the verdict was asked for against the old key, and the new queueing will be taken afresh.
        if (attempt is null || StillWaiting(attempt, claim) is null)
            return;

        var now = clock.UtcNow;
        attempt.RecordEvaluation(claim.QuestionId, evaluation.Verdict, evaluation.Reason, evaluation.Model, now);
        if (!attempt.IsAwaitingEvaluation)
            await ScoreAsync(attempt, now, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await hold.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Records that judging an answer failed, and when to try again. The answer stays in the queue and its attempt waits: a result is not given
    /// before its verdict.
    /// </summary>
    /// <param name="claim">The answer taken off the queue.</param>
    /// <param name="error">What went wrong, in a few words, without the question, the key or the answer in it.</param>
    /// <param name="permanent">Whether asking again will not help until something is changed (a key the service refuses): then the wait is the longest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task FailAsync(GradingClaim claim, string error, bool permanent, CancellationToken cancellationToken)
    {
        var attempt = await attempts.GetByIdAsync(claim.AttemptId, cancellationToken);
        var answer = attempt is null ? null : StillWaiting(attempt, claim);
        if (attempt is null || answer is null)
            return;

        attempt.RecordEvaluationFailure(claim.QuestionId, error, clock.UtcNow + WaitAfter(answer.EvaluationFailures + 1, permanent));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Lets go of a claimed answer that this worker did not get to, so another can take it at once instead of when the lease passes. For a worker that is stopping.</summary>
    /// <param name="claim">The answer taken off the queue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ReleaseAsync(GradingClaim claim, CancellationToken cancellationToken)
    {
        var attempt = await attempts.GetByIdAsync(claim.AttemptId, cancellationToken);
        if (attempt is null || StillWaiting(attempt, claim) is null)
            return;

        attempt.ReleaseEvaluation(claim.QuestionId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // The answer, if it is still waiting for a verdict from the very queueing the claim was taken for.
    private static AttemptAnswer? StillWaiting(Attempt attempt, GradingClaim claim) =>
        attempt.AnswersAwaitingEvaluation.FirstOrDefault(a => a.QuestionId == claim.QuestionId && a.EvaluationRequestedAtUtc == claim.RequestedAtUtc);

    // The wait after the nth failure in a row: the base wait, doubled each time, never longer than the longest. A failure that waiting cannot cure
    // waits the longest from the first, so a refused key is asked about every few minutes and not every half minute.
    private TimeSpan WaitAfter(int failures, bool permanent)
    {
        var settings = options.Value;
        var longest = TimeSpan.FromMinutes(settings.RetryMaxMinutes);
        if (permanent)
            return longest;

        // The exponent is bounded so a long run of failures cannot overflow the arithmetic.
        var wait = TimeSpan.FromSeconds(settings.RetryBaseSeconds * Math.Pow(2, Math.Min(failures - 1, 20)));
        return wait > longest ? longest : wait;
    }

    // Scores the attempt now that every typed answer has its verdict. A graded attempt gets its first score; a submitted one that was queued
    // again after an answer-key correction has its score revised, with the reason given then.
    private async Task ScoreAsync(Attempt attempt, DateTime now, CancellationToken cancellationToken)
    {
        if (attempt.Status != AttemptStatus.Grading && attempt.PendingRevisionReason is null)
            return;

        var exam = await catalog.FindAsync(attempt.ExamId, cancellationToken) ?? throw new ExamContentUnavailableError();
        exam = exam.For(attempt, proctorPolicy);
        var questionIds = exam.Sections.SelectMany(s => s.QuestionIds).ToList();
        var questions = await questionBank.ReadAsync(attempt, questionIds, cancellationToken);
        var result = AttemptScorer.Score(exam, questions, attempt.Answers.ToList());
        attempt.ApplyEvaluatedScore(result.Score, result.MaxScore, now);
    }
}
