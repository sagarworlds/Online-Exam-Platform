using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;

/// <summary>
/// The evaluator the rest of the platform sees: it asks the AI providers that are set up, in order, and falls over to the next when one cannot
/// answer, so a model that has reached its day or is overloaded does not hold up candidates while another could judge. It is also what staff and
/// the worker ask about health: where each provider stands, and whether any can be asked now.
/// </summary>
/// <remarks>
/// A provider that has said "not now" (its <see cref="EvaluatorGate"/> is paused) is passed over without a request. When none can answer, the
/// failure names the first reason and carries how long until any provider can be asked again, so the answer is queued for then and not for a
/// guess. With no provider set up, <see cref="IsEnabled"/> is false and typed answers are marked by the accepted answers alone.
/// </remarks>
public sealed class FallbackTextAnswerEvaluator : ITextAnswerEvaluator, IEvaluatorHealth
{
    private readonly IReadOnlyList<IGatedTextAnswerEvaluator> _providers;
    private readonly ILogger<FallbackTextAnswerEvaluator> _logger;

    /// <summary>Creates the chain.</summary>
    /// <param name="providers">The providers in the order they are asked; one with no key is skipped.</param>
    /// <param name="logger">Where falling over to the next provider is reported, without any content.</param>
    public FallbackTextAnswerEvaluator(IEnumerable<IGatedTextAnswerEvaluator> providers, ILogger<FallbackTextAnswerEvaluator> logger)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled => _providers.Any(provider => provider.IsEnabled);

    /// <inheritdoc />
    public TimeSpan? PausedFor
    {
        get
        {
            TimeSpan? soonest = null;
            var any = false;
            foreach (var provider in _providers.Where(p => p.IsEnabled))
            {
                any = true;
                if (provider.Gate.PausedFor is not { } paused)
                    return null;

                if (soonest is null || paused < soonest)
                    soonest = paused;
            }

            return any ? soonest : null;
        }
    }

    /// <inheritdoc />
    public EvaluatorStatus Status => new(IsEnabled, _providers.Where(p => p.IsEnabled).Select(p => p.Gate.Status).ToList());

    /// <inheritdoc />
    public async Task<TextEvaluation> EvaluateAsync(TextEvaluationRequest request, CancellationToken cancellationToken)
    {
        var enabled = _providers.Where(provider => provider.IsEnabled).ToList();
        if (enabled.Count == 0)
            throw new InvalidOperationException("The AI evaluator is not set up: no provider has a key.");

        var failures = new List<(string Label, TextEvaluationException Failure)>();
        foreach (var provider in enabled)
        {
            var label = $"{provider.Gate.Name} {provider.Gate.Model}";
            if (provider.Gate.PausedFor is { } paused)
            {
                failures.Add((label, new TextEvaluationException($"paused for {CallLoop.Describe(paused)} ({provider.Gate.State})", provider.Gate.IsUnusable, retryAfter: paused)));
                continue;
            }

            try
            {
                return await provider.EvaluateAsync(request, cancellationToken);
            }
            catch (TextEvaluationException failure)
            {
                failures.Add((label, failure));
                _logger.LogDebug("{Provider} could not judge an answer ({Reason}); trying the next provider.", label, failure.Message);
            }
        }

        // The reason that matters is the first one: the provider that was meant to answer, not the last resort.
        var first = failures[0];
        var message = failures.Count == 1 ? $"{first.Label}: {first.Failure.Message}" : $"{first.Label}: {first.Failure.Message} (and {failures.Count - 1} more provider(s) could not answer)";
        throw new TextEvaluationException(message, failures.All(f => f.Failure.IsPermanent), first.Failure, PausedFor);
    }
}
