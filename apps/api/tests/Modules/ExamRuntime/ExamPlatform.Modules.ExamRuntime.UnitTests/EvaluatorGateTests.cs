using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// What every worker of one AI provider shares: how fast requests go out, and the pause that follows a refusal. These are the rules that keep
/// three workers from asking again and again while the provider is saying it has nothing more to give.
/// </summary>
public class EvaluatorGateTests
{
    private static readonly DateTime Start = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    private sealed class LogLines : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    private sealed record Rig(EvaluatorGate Gate, FakeClock Clock, LogLines Log, List<TimeSpan> Waits);

    /// <summary>A gate on a clock the test moves; a wait "takes" exactly as long as it asks, by moving that clock.</summary>
    private static Rig Build(int requestsPerMinute = 0, double jitter = 0)
    {
        var clock = new FakeClock(Start);
        var log = new LogLines();
        var waits = new List<TimeSpan>();
        var gate = new EvaluatorGate("Test", "test-model", requestsPerMinute, clock, log, (delay, _) =>
        {
            waits.Add(delay);
            clock.UtcNow += delay;
            return Task.CompletedTask;
        }, () => jitter);
        return new Rig(gate, clock, log, waits);
    }

    // ---- the requests-per-minute limit ----

    [Fact]
    public async Task WithNoLimit_NoRequestEverWaits()
    {
        var rig = Build(requestsPerMinute: 0);

        for (var i = 0; i < 500; i++)
            await rig.Gate.WaitForTurnAsync(CancellationToken.None);

        Assert.Empty(rig.Waits);
    }

    [Fact]
    public async Task UpToTheLimit_RequestsGoAtOnce_AndTheNextWaitsUntilTheOldestLeavesTheMinute()
    {
        var rig = Build(requestsPerMinute: 3);

        for (var i = 0; i < 3; i++)
            await rig.Gate.WaitForTurnAsync(CancellationToken.None);
        Assert.Empty(rig.Waits);

        await rig.Gate.WaitForTurnAsync(CancellationToken.None);

        Assert.Equal([TimeSpan.FromMinutes(1)], rig.Waits);
        Assert.Equal(Start.AddMinutes(1), rig.Clock.UtcNow);
    }

    [Fact]
    public async Task TheMinuteSlides_SoARequestOnlyWaitsForTheOneThatIsInTheWay()
    {
        var rig = Build(requestsPerMinute: 2);
        await rig.Gate.WaitForTurnAsync(CancellationToken.None);
        rig.Clock.UtcNow = Start.AddSeconds(30);
        await rig.Gate.WaitForTurnAsync(CancellationToken.None);

        await rig.Gate.WaitForTurnAsync(CancellationToken.None);

        // The first request leaves the minute at 60 seconds, 30 seconds after the third asked.
        Assert.Equal([TimeSpan.FromSeconds(30)], rig.Waits);
    }

    [Fact]
    public async Task ARequestAfterAFullMinute_NeedsNoWait()
    {
        var rig = Build(requestsPerMinute: 1);
        await rig.Gate.WaitForTurnAsync(CancellationToken.None);
        rig.Clock.UtcNow = Start.AddSeconds(61);

        await rig.Gate.WaitForTurnAsync(CancellationToken.None);

        Assert.Empty(rig.Waits);
    }

    [Fact]
    public async Task ARequestWaitingForItsTurn_CanBeCancelled()
    {
        var clock = new FakeClock(Start);
        var gate = new EvaluatorGate("Test", "m", 1, clock, new LogLines(), (_, token) => Task.FromCanceled(token));
        await gate.WaitForTurnAsync(CancellationToken.None);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => gate.WaitForTurnAsync(stop.Token));
    }

    // ---- the pauses ----

    [Fact]
    public void ANewGate_CanBeAskedAtOnce()
    {
        var rig = Build();

        Assert.Null(rig.Gate.PausedFor);
        Assert.Equal(ProviderState.Ready, rig.Gate.State);
        Assert.False(rig.Gate.IsUnusable);
    }

    [Fact]
    public void ATooFastRefusal_PausesForTheWaitItNamed_ClampedToWhatIsSensible()
    {
        Assert.Equal(TimeSpan.FromSeconds(55), PauseAfter(g => g.RecordRateLimited(TimeSpan.FromSeconds(55), "x")));
        Assert.Equal(TimeSpan.FromSeconds(1), PauseAfter(g => g.RecordRateLimited(TimeSpan.Zero, "x")));
        Assert.Equal(TimeSpan.FromMinutes(15), PauseAfter(g => g.RecordRateLimited(TimeSpan.FromHours(3), "x")));
    }

    [Fact]
    public void ATooFastRefusalThatNamesNoWait_PausesLongerEachTimeItComes_UpToACeiling()
    {
        var rig = Build();

        var pauses = Enumerable.Range(0, 6).Select(_ => rig.Gate.RecordRateLimited(null, "x")).ToList();

        Assert.Equal(
            [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(240), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)],
            pauses);
    }

    [Fact]
    public void AUsedUpQuota_PausesForHalfAnHour_ThenAnHour()
    {
        var rig = Build();

        var pauses = new[] { rig.Gate.RecordQuotaExhausted("x"), rig.Gate.RecordQuotaExhausted("x"), rig.Gate.RecordQuotaExhausted("x") };

        Assert.Equal([TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(60)], pauses);
        Assert.Equal(ProviderState.QuotaExhausted, rig.Gate.State);
    }

    [Fact]
    public void AnOverloadedService_IsLeftAloneForSecondsAtFirst_ThenForMinutes()
    {
        var rig = Build();

        var pauses = Enumerable.Range(0, 5).Select(_ => rig.Gate.RecordOverloaded("x")).ToList();

        Assert.Equal(
            [TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120)],
            pauses);
    }

    [Fact]
    public void ARefusedKeyOrMissingModel_MakesTheServiceUnusableForTenMinutes()
    {
        var rig = Build();

        var pause = rig.Gate.RecordRefused("403 PERMISSION_DENIED");

        Assert.Equal(TimeSpan.FromMinutes(10), pause);
        Assert.True(rig.Gate.IsUnusable);
        Assert.Equal(ProviderState.Unusable, rig.Gate.State);
    }

    [Fact]
    public void ALongerPause_IsNotShortenedByALighterRefusalBehindIt()
    {
        var rig = Build();
        rig.Gate.RecordQuotaExhausted("day used up");

        rig.Gate.RecordRateLimited(TimeSpan.FromSeconds(10), "too fast");

        Assert.Equal(TimeSpan.FromMinutes(30), rig.Gate.PausedFor);
        Assert.Equal(ProviderState.QuotaExhausted, rig.Gate.State);
        // The lighter refusal is still the last thing that went wrong, and is counted.
        Assert.Equal("too fast", rig.Gate.Status.LastError);
        Assert.Equal(2L, rig.Gate.Status.RateLimited);
    }

    [Fact]
    public void APauseEnds_WhenItsTimeHasPassed()
    {
        var rig = Build();
        rig.Gate.RecordRateLimited(TimeSpan.FromSeconds(20), "x");

        rig.Clock.UtcNow = Start.AddSeconds(21);

        Assert.Null(rig.Gate.PausedFor);
        Assert.Equal(ProviderState.Ready, rig.Gate.State);
        Assert.Null(rig.Gate.Status.PausedUntilUtc);
    }

    [Fact]
    public void AVerdict_EndsAPause_AndTheRefusalsInARowStartAgainFromNone()
    {
        var rig = Build();
        rig.Gate.RecordRateLimited(null, "x");
        rig.Gate.RecordRateLimited(null, "x");

        rig.Gate.RecordSuccess();

        Assert.Null(rig.Gate.PausedFor);
        Assert.Equal(TimeSpan.FromSeconds(30), rig.Gate.RecordRateLimited(null, "x"));
        Assert.NotNull(rig.Gate.Status.LastSuccessAtUtc);
    }

    [Fact]
    public void WorkersThatWereStoppedTogether_DoNotAllStartAgainAtTheSameInstant()
    {
        var rig = Build(jitter: 0.5);

        var pause = rig.Gate.RecordRateLimited(TimeSpan.FromSeconds(100), "x");

        // Up to a fifth longer; here half of that.
        Assert.Equal(TimeSpan.FromSeconds(110), pause);
    }

    [Fact]
    public void ARequestRefusedForItsOwnSake_IsCountedAndRemembered_ButDoesNotPauseAnyone()
    {
        var rig = Build();

        rig.Gate.RecordRefusedRequest("400 INVALID_ARGUMENT: bad argument");

        Assert.Null(rig.Gate.PausedFor);
        Assert.Equal(1L, rig.Gate.Status.Refused);
        Assert.Equal("400 INVALID_ARGUMENT: bad argument", rig.Gate.Status.LastError);
    }

    [Fact]
    public void AnUnusableReply_IsCounted_ButDoesNotPauseAnyone()
    {
        var rig = Build();

        rig.Gate.RecordUnusableReply("The AI service's reply held no verdict.");

        Assert.Null(rig.Gate.PausedFor);
        Assert.Equal(1L, rig.Gate.Status.Unavailable);
    }

    [Fact]
    public void TheReasonKept_IsNeverMoreThanAFewHundredCharacters()
    {
        var rig = Build();

        rig.Gate.RecordOverloaded(new string('x', 5000));

        Assert.Equal(300, rig.Gate.Status.LastError!.Length);
    }

    [Fact]
    public void TheStatus_SaysWhatStaffNeed_AndNothingElse()
    {
        var rig = Build();
        rig.Gate.RecordSuccess();
        rig.Gate.RecordRateLimited(TimeSpan.FromSeconds(40), "429 RESOURCE_EXHAUSTED: slow down");

        var status = rig.Gate.Status;

        Assert.Equal(("Test", "test-model", ProviderState.RateLimited), (status.Name, status.Model, status.State));
        Assert.Equal(Start.AddSeconds(40), status.PausedUntilUtc);
        Assert.Equal(Start, status.LastErrorAtUtc);
        Assert.Equal(Start, status.LastSuccessAtUtc);
        Assert.Equal((1L, 1L), (status.Succeeded, status.RateLimited));
    }

    [Fact]
    public void APause_IsReportedOnce_NotByEveryWorkerThatMeetsIt()
    {
        var rig = Build();

        rig.Gate.RecordRateLimited(TimeSpan.FromSeconds(30), "429 slow down");
        rig.Gate.RecordRateLimited(TimeSpan.FromSeconds(30), "429 slow down");
        rig.Gate.RecordRateLimited(TimeSpan.FromSeconds(30), "429 slow down");

        Assert.Single(rig.Log.Lines);
        Assert.Contains("429 slow down", rig.Log.Lines[0]);
    }

    [Fact]
    public void AnEscalation_FromTooFastToAUsedUpQuota_IsReportedAgain()
    {
        var rig = Build();

        rig.Gate.RecordRateLimited(TimeSpan.FromSeconds(30), "too fast");
        rig.Gate.RecordQuotaExhausted("limit: 0");

        Assert.Equal(2, rig.Log.Lines.Count);
        Assert.Contains("QuotaExhausted", rig.Log.Lines[1]);
    }

    [Fact]
    public async Task ManyWorkersAtOnce_LeaveTheGateConsistent()
    {
        var rig = Build(requestsPerMinute: 1000);

        await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(async () =>
        {
            await rig.Gate.WaitForTurnAsync(CancellationToken.None);
            if (i % 2 == 0)
                rig.Gate.RecordRateLimited(null, "x");
            else
                rig.Gate.RecordSuccess();
            _ = rig.Gate.Status;
            _ = rig.Gate.PausedFor;
        })));

        var status = rig.Gate.Status;
        Assert.Equal(32L, status.RateLimited);
        Assert.Equal(32L, status.Succeeded);
    }

    private static TimeSpan PauseAfter(Func<EvaluatorGate, TimeSpan> record) => record(Build().Gate);
}
