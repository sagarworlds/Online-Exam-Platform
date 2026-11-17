using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>The nudge that wakes the grading worker: standing, not counted, and never lost between a nudge and the wait.</summary>
public class GradingWakeUpTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task ANudgeBeforeTheWait_IsNotLost()
    {
        var wakeUp = new GradingWakeUp();

        wakeUp.Notify();

        Assert.True(await wakeUp.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    [Fact]
    public async Task WithNoNudge_TheWaitEndsWhenTheTimeRunsOut()
    {
        var wakeUp = new GradingWakeUp();

        Assert.False(await wakeUp.WaitAsync(Short, CancellationToken.None));
    }

    [Fact]
    public async Task ManyNudgesBeforeTheWorkerLooks_AreOneWakeUp()
    {
        var wakeUp = new GradingWakeUp();

        wakeUp.Notify();
        wakeUp.Notify();
        wakeUp.Notify();

        Assert.True(await wakeUp.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.False(await wakeUp.WaitAsync(Short, CancellationToken.None));
    }

    [Fact]
    public async Task ANudgeDuringTheWait_EndsItAtOnce()
    {
        var wakeUp = new GradingWakeUp();
        var waiting = wakeUp.WaitAsync(null, CancellationToken.None);

        Assert.False(waiting.IsCompleted);
        wakeUp.Notify();

        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ANegativeOrHugeWait_IsAccepted()
    {
        var wakeUp = new GradingWakeUp();
        wakeUp.Notify();

        Assert.True(await wakeUp.WaitAsync(TimeSpan.FromSeconds(-5), CancellationToken.None));

        wakeUp.Notify();
        Assert.True(await wakeUp.WaitAsync(TimeSpan.FromDays(400), CancellationToken.None));
    }

    [Fact]
    public async Task AStoppingHost_EndsTheWaitWithACancellation()
    {
        var wakeUp = new GradingWakeUp();
        using var stopping = new CancellationTokenSource();
        var waiting = wakeUp.WaitAsync(null, stopping.Token);

        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}

/// <summary>How long the grading worker may sleep: until the next retry, the end of a pause or the give-up deadline, and not at all when nothing waits.</summary>
public class NextWakeTests
{
    private static readonly DateTime Now = Fixtures.Now.AddHours(3);

    private readonly FakeClock _clock = new(Now);
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly AiGradingOptions _settings = new();

    private TypedAnswerGrading Grading => new(
        _attempts,
        Substitute.For<IExamCatalog>(),
        Substitute.For<IQuestionBank>(),
        Substitute.For<IExamRuntimeUnitOfWork>(),
        new ProctorPolicy(),
        _clock,
        Options.Create(_settings));

    private void Queue(DateTime? claimableAt, DateTime? oldestInGrading) =>
        _attempts.GetGradingScheduleAsync(Arg.Any<CancellationToken>()).Returns(new GradingSchedule(claimableAt, oldestInGrading));

    [Fact]
    public async Task WithNothingWaiting_TheWorkerSleepsUntilNudged()
    {
        Queue(null, null);

        Assert.Null(await Grading.NextWakeAsync(evaluatorOn: true, pausedFor: null, CancellationToken.None));
    }

    [Fact]
    public async Task AnAnswerWhoseRetryFallsDueLater_WakesTheWorkerThen()
    {
        Queue(Now.AddMinutes(5), Now.AddMinutes(-1));

        var wait = await Grading.NextWakeAsync(evaluatorOn: true, pausedFor: null, CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(5), wait);
    }

    [Fact]
    public async Task AnAnswerThatCanBeTakenNow_IsLookedAtAfterTheShortestWaitAndNotInATightLoop()
    {
        Queue(DateTime.MinValue, Now.AddMinutes(-1));
        _settings.PollSeconds = 10;

        var wait = await Grading.NextWakeAsync(evaluatorOn: true, pausedFor: null, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(10), wait);
    }

    [Fact]
    public async Task APauseOfTheProviders_DelaysTheWakeUntilItEnds()
    {
        Queue(DateTime.MinValue, null);

        var wait = await Grading.NextWakeAsync(evaluatorOn: true, pausedFor: TimeSpan.FromMinutes(2), CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(2), wait);
    }

    [Fact]
    public async Task TheGiveUpDeadline_WakesTheWorkerBeforeALaterRetry()
    {
        // Queued 10 minutes ago, so the 30-minute wait ends in 20; the retry would not fall due for an hour.
        Queue(Now.AddHours(1), Now.AddMinutes(-10));

        var wait = await Grading.NextWakeAsync(evaluatorOn: true, pausedFor: null, CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(20), wait);
    }

    [Fact]
    public async Task WithNoEvaluator_OnlyTheGiveUpDeadlineCounts()
    {
        Queue(DateTime.MinValue, Now.AddMinutes(-10));

        var wait = await Grading.NextWakeAsync(evaluatorOn: false, pausedFor: null, CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(20), wait);
    }

    [Fact]
    public async Task WithNoEvaluatorAndNeverGivingUp_TheWorkerSleepsUntilNudged()
    {
        _settings.MaxWaitMinutes = 0;
        Queue(DateTime.MinValue, Now.AddMinutes(-10));

        Assert.Null(await Grading.NextWakeAsync(evaluatorOn: false, pausedFor: null, CancellationToken.None));
    }

    [Fact]
    public async Task AnAttemptAlreadyPastItsDeadline_IsLookedAtAfterTheShortestWait()
    {
        // A worker on another host holds the answer, so it was not given up on; looked at again soon, but not in a tight loop.
        Queue(null, Now.AddMinutes(-45));

        var wait = await Grading.NextWakeAsync(evaluatorOn: false, pausedFor: null, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(_settings.PollSeconds), wait);
    }
}
