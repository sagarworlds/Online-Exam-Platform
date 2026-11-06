using ExamPlatform.Modules.ExamRuntime.Application.Ports;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;

/// <summary>
/// What kind of failure one request ended in. It decides what is done about it: <see cref="Overloaded"/> and <see cref="BadReply"/> are tried
/// again within the call, the others are not.
/// </summary>
internal enum FailureKind
{
    /// <summary>Overloaded, unreachable or too slow: likely to pass, so it is tried again within the call.</summary>
    Overloaded,

    /// <summary>The provider answered, but not with a usable verdict: asking again may get one.</summary>
    BadReply,

    /// <summary>Asked too fast: asking again at once only adds failed requests, so the provider is paused for everyone.</summary>
    RateLimited,

    /// <summary>A quota is used up, or is zero for this key and model: a long pause.</summary>
    QuotaExhausted,

    /// <summary>The key, the model or the place was refused: nothing changes until a setting does.</summary>
    Refused,

    /// <summary>This one request was refused for a reason about the request itself.</summary>
    RefusedRequest,
}

/// <summary>The result of one request to a provider: a verdict, or a failure of some kind.</summary>
/// <param name="Evaluation">The verdict, or null on failure.</param>
/// <param name="Failure">The failure, or null on success.</param>
/// <param name="Kind">What kind of failure it was.</param>
/// <param name="RetryAfter">How long the provider asked us to wait, when it said.</param>
/// <param name="Detail">The provider's own reason in a few words, for the log and for staff; never any content.</param>
internal readonly record struct Outcome(
    TextEvaluation? Evaluation, TextEvaluationException? Failure, FailureKind Kind = FailureKind.Overloaded, TimeSpan? RetryAfter = null, string? Detail = null)
{
    public static Outcome Done(TextEvaluation evaluation) => new(evaluation, null);

    public static Outcome Failed(FailureKind kind, string reason, TimeSpan? retryAfter = null, string? detail = null) =>
        new(null, new TextEvaluationException(reason, kind is FailureKind.Refused or FailureKind.RefusedRequest), kind, retryAfter, detail ?? reason);
}
