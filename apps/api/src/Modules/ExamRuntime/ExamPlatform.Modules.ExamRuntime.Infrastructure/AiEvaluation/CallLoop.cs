using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;

/// <summary>How one call to a provider is made: its tries, the waits between them, and the gate that is shared by every worker.</summary>
/// <param name="Gate">The provider's shared pacing and pause.</param>
/// <param name="MaxTries">The most requests one call sends.</param>
/// <param name="RetryBaseDelaySeconds">The wait before the second try; each later one doubles.</param>
/// <param name="RetryMaxDelaySeconds">The longest wait between two tries.</param>
/// <param name="Wait">How to wait; the real delay unless a test replaces it.</param>
/// <param name="Logger">Where a retry is reported, without any content.</param>
internal sealed record CallPlan(
    EvaluatorGate Gate, int MaxTries, double RetryBaseDelaySeconds, double RetryMaxDelaySeconds, Func<TimeSpan, CancellationToken, Task> Wait, ILogger Logger);

/// <summary>
/// What every provider does around one request, so that the providers behave the same and a rule is written once: look at the pause, wait for
/// a turn, send, and decide from how it failed whether to try again, to pause the provider for everyone, or to give up on the call.
/// </summary>
internal static class CallLoop
{
    /// <summary>Makes one call: up to <see cref="CallPlan.MaxTries"/> requests, stopping early when asking again would not help.</summary>
    /// <param name="plan">The tries, waits and gate.</param>
    /// <param name="tryOnce">Sends one request and says how it ended.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="TextEvaluationException">No verdict could be had; says whether asking again can help, and when.</exception>
    public static async Task<TextEvaluation> RunAsync(CallPlan plan, Func<CancellationToken, Task<Outcome>> tryOnce, CancellationToken cancellationToken)
    {
        var gate = plan.Gate;
        ThrowIfPaused(gate);

        Outcome last = default;
        for (var attempt = 1; attempt <= plan.MaxTries; attempt++)
        {
            await gate.WaitForTurnAsync(cancellationToken);
            var outcome = await tryOnce(cancellationToken);
            if (outcome.Evaluation is { } evaluation)
            {
                gate.RecordSuccess();
                return evaluation;
            }

            var failure = outcome.Failure!;
            switch (outcome.Kind)
            {
                case FailureKind.RateLimited:
                {
                    var pause = gate.RecordRateLimited(outcome.RetryAfter, outcome.Detail!);
                    throw new TextEvaluationException($"{failure.Message} It is being asked too fast; paused for {Describe(pause)}.", false, retryAfter: pause);
                }

                case FailureKind.QuotaExhausted:
                {
                    var pause = gate.RecordQuotaExhausted(outcome.Detail!, outcome.RetryAfter);
                    throw new TextEvaluationException(
                        $"{failure.Message} Its quota is used up, or is zero for this key and model; paused for {Describe(pause)}.", false, retryAfter: pause);
                }

                case FailureKind.Refused:
                {
                    var pause = gate.RecordRefused(outcome.Detail!);
                    throw new TextEvaluationException(failure.Message, true, retryAfter: pause);
                }

                case FailureKind.RefusedRequest:
                    gate.RecordRefusedRequest(outcome.Detail!);
                    throw failure;
            }

            last = outcome;
            if (attempt == plan.MaxTries)
                break;

            // Another worker may have found the provider busy while this one was waiting; asking on would only add to it.
            if (gate.PausedFor is { } paused)
                throw new TextEvaluationException(failure.Message, false, failure, paused);

            var delay = DelayBefore(attempt + 1, outcome.RetryAfter, plan);
            plan.Logger.LogWarning(
                "Try {Attempt} of {MaxTries} at asking the AI evaluator failed ({Reason}); trying again in {Delay:0.##} seconds.",
                attempt, plan.MaxTries, failure.Message, delay.TotalSeconds);
            await plan.Wait(delay, cancellationToken);
        }

        TimeSpan? pausedFor = null;
        if (last.Kind == FailureKind.Overloaded)
            pausedFor = gate.RecordOverloaded(last.Detail!);
        else
            gate.RecordUnusableReply(last.Detail!);

        throw new TextEvaluationException($"{last.Failure!.Message} It was tried {plan.MaxTries} times.", false, last.Failure, pausedFor);
    }

    /// <summary>Says a span in the few words a person reads: seconds up to two minutes, minutes after.</summary>
    /// <param name="span">The span.</param>
    public static string Describe(TimeSpan span) =>
        span.TotalMinutes >= 2
            ? $"{Math.Round(span.TotalMinutes):0} minutes"
            : $"{Math.Ceiling(span.TotalSeconds):0} seconds";

    private static void ThrowIfPaused(EvaluatorGate gate)
    {
        if (gate.PausedFor is not { } paused)
            return;

        throw new TextEvaluationException($"The AI service is paused for {Describe(paused)} ({gate.State}).", gate.IsUnusable, retryAfter: paused);
    }

    // The wait before try number `attempt` (the second try waits the base delay, the third twice that, and so on), or what the provider asked
    // for when it said how long to wait, either way no longer than the most a call is willing to wait.
    private static TimeSpan DelayBefore(int attempt, TimeSpan? requested, CallPlan plan)
    {
        var ceiling = TimeSpan.FromSeconds(plan.RetryMaxDelaySeconds);
        var doubling = TimeSpan.FromSeconds(plan.RetryBaseDelaySeconds * Math.Pow(2, attempt - 2));
        var wanted = requested ?? doubling;
        return wanted > ceiling ? ceiling : wanted;
    }
}
