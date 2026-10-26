namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>Where one AI provider stands, from what the platform has seen of it since the host started.</summary>
public enum ProviderState
{
    /// <summary>Nothing is wrong: it has answered, or has not yet been asked.</summary>
    Ready,

    /// <summary>It said it is being asked too fast, and is left alone for the wait it named.</summary>
    RateLimited,

    /// <summary>A quota (usually the day's) is used up, or is zero for this key and model. Asking sooner than the pause is wasted.</summary>
    QuotaExhausted,

    /// <summary>It is overloaded or unreachable, and is left alone for a short while.</summary>
    Overloaded,

    /// <summary>It refused the key, or does not have the model. Nothing changes until a setting does.</summary>
    Unusable,
}

/// <summary>One provider as staff see it. Never carries a question, an accepted answer or a typed answer.</summary>
/// <param name="Name">The provider's name, such as "OpenAiCompatible" or "Gemini".</param>
/// <param name="Model">The model it is asked to use.</param>
/// <param name="State">Where it stands now.</param>
/// <param name="PausedUntilUtc">When it will be asked again, or null when it is not paused.</param>
/// <param name="LastError">The last failure, in a few words and with the provider's own reason, or null if none.</param>
/// <param name="LastErrorAtUtc">When that failure happened.</param>
/// <param name="LastSuccessAtUtc">When it last gave a verdict, or null if it has not since the host started.</param>
/// <param name="Succeeded">Verdicts it has given since the host started.</param>
/// <param name="RateLimited">Times it said it was being asked too fast, or that a quota is used up.</param>
/// <param name="Unavailable">Times it was overloaded, unreachable or gave nothing usable.</param>
/// <param name="Refused">Times it refused a request or the key.</param>
public sealed record ProviderStatus(
    string Name, string Model, ProviderState State, DateTime? PausedUntilUtc, string? LastError, DateTime? LastErrorAtUtc,
    DateTime? LastSuccessAtUtc, long Succeeded, long RateLimited, long Unavailable, long Refused);

/// <summary>The AI evaluators that are set up, and where each stands.</summary>
/// <param name="Enabled">Whether at least one provider is set up. When none is, typed answers are marked by the accepted answers alone.</param>
/// <param name="Providers">The set-up providers, in the order they are asked.</param>
public sealed record EvaluatorStatus(bool Enabled, IReadOnlyList<ProviderStatus> Providers);

/// <summary>
/// Whether the AI evaluators can be asked right now. The worker looks before it takes an answer off the queue, so a provider that has said
/// "not now" is left alone instead of being asked, failing, and having the failure counted against an answer.
/// </summary>
public interface IEvaluatorHealth
{
    /// <summary>
    /// How long until at least one set-up provider can be asked again; null when one can be asked now, and also when none is set up (then
    /// nothing is asked at all).
    /// </summary>
    TimeSpan? PausedFor { get; }

    /// <summary>Each set-up provider and where it stands, for staff.</summary>
    EvaluatorStatus Status { get; }
}
