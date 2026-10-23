using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.SharedKernel.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Endpoints.Grading;

/// <summary>
/// Works through the typed answers that wait for the AI evaluator: a few workers each take one answer off the queue, ask the evaluator about
/// it, and save the verdict, until none is due. An attempt is scored by whichever worker saves its last verdict.
/// </summary>
/// <remarks>
/// <para>
/// Safe to run on several hosts, and from a timer and by hand at once: an answer is taken with a lease, so only one worker holds it, and a worker
/// that dies lets go when the lease passes. Each step has a scope of its own. The ask can take minutes, so no database transaction is held open
/// across it, and what is saved is read fresh from the database.
/// </para>
/// <para>
/// A failure never loses an answer or marks it wrong. It is recorded with a wait, longer each time, and the attempt stays in grading until
/// a verdict comes, or until <see cref="AiGradingOptions.MaxWaitMinutes"/> have passed: then the answers still without a verdict are marked
/// by the accepted answers alone, with a note on each, and the attempt is scored. Only counts and the failure's kind are logged: never the
/// question, the key's answers, or what the candidate typed.
/// </para>
/// </remarks>
public sealed class TypedAnswerGradingRunner(IServiceScopeFactory scopes, IOptions<AiGradingOptions> options, ILogger<TypedAnswerGradingRunner> logger)
{
    // A verdict that clashes with another request for the same attempt is saved again from a fresh read; the verdict is in hand, so no new ask is made.
    private const int SaveTries = 3;

    /// <summary>Whether the evaluator is set up. Without it the queue is left alone: the attempts wait for a host that has it.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> IsEvaluatorOnAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<ITextAnswerEvaluator>().IsEnabled;
    }

    /// <summary>
    /// Gives up on attempts that have waited in grading too long, then takes answers off the queue until none is due. The giving up comes first
    /// and does not need the evaluator: a candidate must not wait for ever on one that is down, or was switched off after they submitted.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many answers were handled, whether they got a verdict or a failure to try again.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var evaluatorOn = await IsEvaluatorOnAsync(cancellationToken);
        // With no evaluator nothing new is queued, so only attempts left over from before need this, and a look every few minutes is enough.
        // That also keeps a host with no key from touching the database on every tick.
        if (evaluatorOn || OffSweepIsDue())
            await GiveUpOverdueAsync(cancellationToken);

        if (!evaluatorOn)
            return 0;

        var workers = Enumerable.Range(0, options.Value.Concurrency).Select(_ => WorkAsync(cancellationToken)).ToArray();
        var handled = (await Task.WhenAll(workers)).Sum();
        if (handled > 0)
            logger.LogInformation("AI grading handled {Count} typed answers.", handled);

        return handled;
    }

    // How often a host with no evaluator looks for attempts that have waited too long.
    private static readonly long OffSweepEveryMilliseconds = (long)TimeSpan.FromMinutes(5).TotalMilliseconds;

    // The tick count at which a host with no evaluator next looks. Only the one timer calls RunOnceAsync, so it needs no lock.
    private long _nextOffSweepAt;

    private bool OffSweepIsDue()
    {
        if (options.Value.MaxWaitMinutes == 0)
            return false;

        var now = Environment.TickCount64;
        if (now < _nextOffSweepAt)
            return false;

        _nextOffSweepAt = now + OffSweepEveryMilliseconds;
        return true;
    }

    private async Task GiveUpOverdueAsync(CancellationToken cancellationToken)
    {
        if (options.Value.MaxWaitMinutes == 0)
            return;

        IReadOnlyList<Guid> overdue;
        await using (var scope = scopes.CreateAsyncScope())
            overdue = await scope.ServiceProvider.GetRequiredService<TypedAnswerGrading>().FindOverdueAsync(50, cancellationToken);

        var gaveUp = 0;
        foreach (var attemptId in overdue)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                gaveUp += await scope.ServiceProvider.GetRequiredService<TypedAnswerGrading>().GiveUpAsync(attemptId, cancellationToken);
            }
            catch (ConcurrencyConflictError)
            {
                // Another request changed the attempt at the same moment; it is looked at again at the next pass.
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "An attempt that has waited too long for the AI evaluator could not be scored; it is tried again at the next pass.");
            }
        }

        if (gaveUp > 0)
        {
            logger.LogWarning(
                "AI grading gave up waiting for {Count} typed answers after {Minutes} minutes and marked them by the accepted answers alone.",
                gaveUp, options.Value.MaxWaitMinutes);
        }
    }

    private async Task<int> WorkAsync(CancellationToken cancellationToken)
    {
        var handled = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            GradingClaim? claim;
            await using (var scope = scopes.CreateAsyncScope())
                claim = (await scope.ServiceProvider.GetRequiredService<TypedAnswerGrading>().ClaimAsync(1, cancellationToken)).FirstOrDefault();

            if (claim is null)
                break;

            try
            {
                await HandleAsync(claim, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The host is stopping, as on a deploy: let go of the answer so the next host takes it at once, not when the lease passes.
                await ReleaseAsync(claim);
                throw;
            }

            handled++;
        }

        return handled;
    }

    private async Task ReleaseAsync(GradingClaim claim)
    {
        try
        {
            // Not the host's token, which is spent; a few seconds is all a release may take.
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TypedAnswerGrading>().ReleaseAsync(claim, limit.Token);
        }
        catch (Exception exception)
        {
            // Not fatal: the answer is taken again when its lease passes.
            logger.LogWarning(exception, "A typed answer held by a stopping host could not be let go; it is taken again when its lease passes.");
        }
    }

    private async Task HandleAsync(GradingClaim claim, CancellationToken cancellationToken)
    {
        TextEvaluation evaluation;
        try
        {
            TextEvaluationRequest? request;
            await using (var scope = scopes.CreateAsyncScope())
                request = await scope.ServiceProvider.GetRequiredService<TypedAnswerGrading>().PrepareAsync(claim, cancellationToken);

            // Not waiting for a verdict any more: another host, or a correction of the key, got there first.
            if (request is null)
                return;

            await using (var scope = scopes.CreateAsyncScope())
                evaluation = await scope.ServiceProvider.GetRequiredService<ITextAnswerEvaluator>().EvaluateAsync(request, cancellationToken);
        }
        catch (TextEvaluationException failure)
        {
            await FailAsync(claim, failure.Message, failure.IsPermanent, cancellationToken);
            return;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "A typed answer could not be prepared for the AI evaluator; it will be tried again.");
            await FailAsync(claim, "The answer could not be prepared for the evaluator.", permanent: false, cancellationToken);
            return;
        }

        await SaveAsync(claim, evaluation, cancellationToken);
    }

    private async Task SaveAsync(GradingClaim claim, TextEvaluation evaluation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TypedAnswerGrading>().RecordAsync(claim, evaluation, cancellationToken);
                return;
            }
            catch (ConcurrencyConflictError) when (attempt < SaveTries && !cancellationToken.IsCancellationRequested)
            {
                // Another request changed the same attempt at the same moment: read it again and save the verdict once more.
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The verdict itself is lost with the failed save, so the answer is asked about again after a wait. The attempt's score may be
                // what could not be worked out (an exam or question that is gone), which no ask will cure; the wait grows each time.
                logger.LogError(exception, "The AI evaluator's verdict could not be saved or the attempt could not be scored; the answer will be asked about again.");
                await FailAsync(claim, "The verdict could not be saved.", permanent: false, cancellationToken);
                return;
            }
        }
    }

    private async Task FailAsync(GradingClaim claim, string error, bool permanent, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TypedAnswerGrading>().FailAsync(claim, error, permanent, cancellationToken);
            logger.LogWarning("Asking the AI evaluator about a typed answer failed ({Reason}); it will be tried again later.", error);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Not even the failure could be saved. The answer is still held by its lease and is taken again when the lease passes.
            logger.LogError(exception, "A failure to ask the AI evaluator could not be recorded; the answer will be taken again when its lease passes.");
        }
    }
}
