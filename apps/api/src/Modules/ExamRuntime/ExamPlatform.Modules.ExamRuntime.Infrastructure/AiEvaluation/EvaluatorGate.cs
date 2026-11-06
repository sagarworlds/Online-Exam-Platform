using System.Security.Cryptography;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;

/// <summary>
/// What every worker of one AI provider shares: how fast requests may be sent, and whether the provider has said "not now". One instance per
/// provider, living as long as the host.
/// </summary>
/// <remarks>
/// <para>
/// Without it each worker handled a refusal on its own: three of them kept asking, four times in seven seconds, while the provider was telling
/// all of them it had no more to give. With it, the first refusal pauses the provider for everyone for as long as the provider asked, or for a
/// wait that grows with each refusal in a row when it did not say, and the worker stops taking answers off the queue until the pause is over.
/// </para>
/// <para>
/// A pause never shortens: a day's quota that is used up is not forgotten because a later, lighter refusal arrives. A verdict ends the pause,
/// because it proves the provider is answering. Nothing here ever holds a question, an accepted answer or a typed answer; only a few words about
/// why the provider refused.
/// </para>
/// </remarks>
public sealed class EvaluatorGate
{
    // The wait after the first "too fast" in a row, doubled for each one after it. A wait the provider names is used instead, up to MaxNamedWait.
    internal static readonly TimeSpan RateLimitedFirst = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan RateLimitedCap = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan MaxNamedWait = TimeSpan.FromMinutes(15);

    // A used-up quota is mostly the day's, which minutes of waiting do not cure, so the first pause is long and the next longer. Each one
    // ends with a single request that finds out whether the quota is back.
    internal static readonly TimeSpan QuotaFirst = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan QuotaCap = TimeSpan.FromMinutes(60);
    internal static readonly TimeSpan QuotaNamedCap = TimeSpan.FromHours(6);

    // Overload passes in seconds or minutes.
    internal static readonly TimeSpan OverloadedFirst = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan OverloadedCap = TimeSpan.FromMinutes(2);

    // A refused key or a missing model changes only when someone changes a setting; a check every ten minutes finds out when they have.
    internal static readonly TimeSpan UnusableFor = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // Spread over this share of a pause, so workers that were stopped together do not all start again at the same instant.
    private const double JitterShare = 0.2;

    private readonly object _lock = new();
    private readonly Queue<DateTime> _recent = new();
    private readonly int _requestsPerMinute;
    private readonly Clock _clock;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private readonly Func<double> _jitter;

    private ProviderState _state = ProviderState.Ready;
    private DateTime? _pausedUntil;
    private string? _lastError;
    private DateTime? _lastErrorAt;
    private DateTime? _lastSuccess;
    private long _succeeded;
    private long _rateLimited;
    private long _unavailable;
    private long _refused;
    private int _rateLimitedStreak;
    private int _quotaStreak;
    private int _overloadedStreak;

    /// <summary>Creates the gate for one provider.</summary>
    /// <param name="name">The provider's name, as staff see it.</param>
    /// <param name="model">The model asked, as staff see it.</param>
    /// <param name="requestsPerMinute">The most requests to send in any minute; 0 or less for no limit of our own.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="logger">Where a pause is reported, once.</param>
    /// <param name="wait">How to wait for a turn; the real delay unless a test replaces it.</param>
    /// <param name="jitter">A number from 0 up to but not including 1; random unless a test replaces it.</param>
    public EvaluatorGate(
        string name, string model, int requestsPerMinute, Clock clock, ILogger logger,
        Func<TimeSpan, CancellationToken, Task>? wait = null, Func<double>? jitter = null)
    {
        Name = name;
        Model = model;
        _requestsPerMinute = requestsPerMinute;
        _clock = clock;
        _logger = logger;
        _wait = wait ?? ((delay, token) => Task.Delay(delay, token));
        // CA5394 asks for a cryptographic source for anything that could be security-relevant; this only spreads out restarts.
        _jitter = jitter ?? (() => RandomNumberGenerator.GetInt32(0, 1000) / 1000.0);
    }

    /// <summary>The provider's name.</summary>
    public string Name { get; }

    /// <summary>The model asked.</summary>
    public string Model { get; }

    /// <summary>How long until the provider is asked again; null when it can be asked now.</summary>
    public TimeSpan? PausedFor
    {
        get
        {
            lock (_lock)
                return RemainingLocked(_clock.UtcNow);
        }
    }

    /// <summary>Whether the provider is refusing until a setting is changed (a key it rejects, a model it does not have).</summary>
    public bool IsUnusable
    {
        get
        {
            lock (_lock)
                return RemainingLocked(_clock.UtcNow) is not null && _state == ProviderState.Unusable;
        }
    }

    /// <summary>The state the provider is in now.</summary>
    public ProviderState State
    {
        get
        {
            lock (_lock)
                return RemainingLocked(_clock.UtcNow) is null ? ProviderState.Ready : _state;
        }
    }

    /// <summary>Where the provider stands, for staff.</summary>
    public ProviderStatus Status
    {
        get
        {
            lock (_lock)
            {
                var paused = RemainingLocked(_clock.UtcNow) is not null;
                return new ProviderStatus(
                    Name, Model, paused ? _state : ProviderState.Ready, paused ? _pausedUntil : null, _lastError, _lastErrorAt, _lastSuccess,
                    _succeeded, _rateLimited, _unavailable, _refused);
            }
        }
    }

    /// <summary>
    /// Waits until a request may be sent without passing the requests-per-minute limit, and takes the place for it. Returns at once when
    /// there is no limit.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WaitForTurnAsync(CancellationToken cancellationToken)
    {
        if (_requestsPerMinute <= 0)
            return;

        while (true)
        {
            TimeSpan wait;
            lock (_lock)
            {
                var now = _clock.UtcNow;
                while (_recent.Count > 0 && now - _recent.Peek() >= Window)
                    _recent.Dequeue();

                if (_recent.Count < _requestsPerMinute)
                {
                    _recent.Enqueue(now);
                    return;
                }

                // When the oldest request leaves the minute, there is room for one more.
                wait = _recent.Peek() + Window - now;
            }

            await _wait(wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    /// <summary>A verdict came back: the provider is answering, so any pause ends and the refusals in a row start again from none.</summary>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            _succeeded++;
            _lastSuccess = _clock.UtcNow;
            _pausedUntil = null;
            _state = ProviderState.Ready;
            _rateLimitedStreak = 0;
            _quotaStreak = 0;
            _overloadedStreak = 0;
        }
    }

    /// <summary>The provider said it is being asked too fast.</summary>
    /// <param name="named">How long it asked us to wait, when it said.</param>
    /// <param name="detail">Its own reason, in a few words and without any content.</param>
    /// <returns>How long it is now left alone.</returns>
    public TimeSpan RecordRateLimited(TimeSpan? named, string detail)
    {
        TimeSpan pause;
        lock (_lock)
        {
            _rateLimited++;
            _rateLimitedStreak++;
            pause = named is { } asked
                ? Max(TimeSpan.FromSeconds(1), Min(asked, MaxNamedWait))
                : Min(Grow(RateLimitedFirst, _rateLimitedStreak), RateLimitedCap);
        }

        return Pause(ProviderState.RateLimited, pause, detail);
    }

    /// <summary>A quota is used up, or is zero for this key and model.</summary>
    /// <param name="detail">The provider's own reason, in a few words and without any content.</param>
    /// <param name="named">
    /// When the provider says exactly when the quota is back (a message that reads "try again in 14m"), the wait it named. Not every provider's
    /// word can be trusted for a day's quota, so a caller passes it only when it can.
    /// </param>
    /// <returns>How long it is now left alone.</returns>
    public TimeSpan RecordQuotaExhausted(string detail, TimeSpan? named = null)
    {
        TimeSpan pause;
        lock (_lock)
        {
            _rateLimited++;
            _quotaStreak++;
            pause = named is { } asked
                ? Max(TimeSpan.FromMinutes(1), Min(asked, QuotaNamedCap))
                : Min(Grow(QuotaFirst, _quotaStreak), QuotaCap);
        }

        return Pause(ProviderState.QuotaExhausted, pause, detail);
    }

    /// <summary>The provider is overloaded, or could not be reached, after the tries one call makes.</summary>
    /// <param name="detail">What went wrong, in a few words and without any content.</param>
    /// <returns>How long it is now left alone.</returns>
    public TimeSpan RecordOverloaded(string detail)
    {
        TimeSpan pause;
        lock (_lock)
        {
            _unavailable++;
            _overloadedStreak++;
            pause = Min(Grow(OverloadedFirst, _overloadedStreak), OverloadedCap);
        }

        return Pause(ProviderState.Overloaded, pause, detail);
    }

    /// <summary>The provider rejected the key or does not have the model: nothing changes until a setting does.</summary>
    /// <param name="detail">The provider's own reason, in a few words and without any content.</param>
    /// <returns>How long it is now left alone.</returns>
    public TimeSpan RecordRefused(string detail)
    {
        lock (_lock)
            _refused++;

        return Pause(ProviderState.Unusable, UnusableFor, detail);
    }

    /// <summary>
    /// The provider refused one request for a reason that is about that request, not about the key or the model. Counted and remembered, but
    /// the provider is not paused: the next answer may be perfectly acceptable.
    /// </summary>
    /// <param name="detail">The provider's own reason, in a few words and without any content.</param>
    public void RecordRefusedRequest(string detail)
    {
        lock (_lock)
        {
            _refused++;
            Remember(detail);
        }
    }

    /// <summary>The provider answered, but not with a usable verdict. Counted and remembered; the provider is not paused for it.</summary>
    /// <param name="detail">What was wrong with the reply, in a few words and without any content.</param>
    public void RecordUnusableReply(string detail)
    {
        lock (_lock)
        {
            _unavailable++;
            Remember(detail);
        }
    }

    private TimeSpan Pause(ProviderState state, TimeSpan pause, string detail)
    {
        var withJitter = pause + TimeSpan.FromTicks((long)(pause.Ticks * JitterShare * _jitter()));
        bool report;
        TimeSpan remaining;
        lock (_lock)
        {
            var now = _clock.UtcNow;
            var wasPaused = RemainingLocked(now) is not null;
            var previousState = _state;
            var until = now + withJitter;

            // The longer pause stands: a day's quota is not forgotten because a lighter refusal arrives behind it.
            var applied = false;
            if (_pausedUntil is not { } existing || existing <= now || until > existing)
            {
                _pausedUntil = until;
                _state = state;
                applied = true;
            }

            Remember(detail);
            remaining = _pausedUntil!.Value - now;
            report = !wasPaused || (applied && previousState != state);
        }

        // Reported once for the pause that started it, not by every worker that meets the same refusal.
        if (report)
        {
            _logger.LogWarning(
                "{Provider} ({Model}) is paused for {Seconds:0} seconds ({State}): {Detail}", Name, Model, remaining.TotalSeconds, state, detail);
        }

        return remaining;
    }

    private void Remember(string detail)
    {
        _lastError = detail.Length <= 300 ? detail : detail[..300];
        _lastErrorAt = _clock.UtcNow;
    }

    private TimeSpan? RemainingLocked(DateTime now) =>
        _pausedUntil is { } until && until > now ? until - now : null;

    // first, twice first, four times first, and so on; the exponent is bounded so a long run of refusals cannot overflow.
    private static TimeSpan Grow(TimeSpan first, int streak) =>
        TimeSpan.FromTicks(first.Ticks * (1L << Math.Min(streak - 1, 20)));

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
