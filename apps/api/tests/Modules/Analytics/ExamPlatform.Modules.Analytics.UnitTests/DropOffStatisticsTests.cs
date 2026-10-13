using ExamPlatform.Modules.Analytics.Domain;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>Where candidates stop an exam: the drop-off rule that the item analysis reports (FR-37).</summary>
public class DropOffStatisticsTests
{
    private readonly Guid _first = Guid.NewGuid();
    private readonly Guid _second = Guid.NewGuid();
    private readonly Guid _third = Guid.NewGuid();

    /// <summary>A paper of the three questions, each answered or not as the flags say, in paper order.</summary>
    private CandidatePaper Paper(bool endedByClock, bool a, bool b, bool c) =>
        new(endedByClock, [new PaperQuestion(_first, a), new PaperQuestion(_second, b), new PaperQuestion(_third, c)]);

    [Fact]
    public void ACandidateWhoAnsweredNothing_GivesNoPoint_EvenWhenTheClockEndedThePaper()
    {
        var counts = DropOffStatistics.Of([Paper(endedByClock: true, a: false, b: false, c: false)]);

        Assert.Empty(counts);
    }

    [Fact]
    public void ACandidateWhoAnsweredEveryQuestion_DropsOffNowhere_EvenWhenTheClockEndedThePaper()
    {
        var counts = DropOffStatistics.Of([Paper(endedByClock: true, a: true, b: true, c: true)]);

        Assert.Equal(new DropOffCount(1, 0), counts[_first]);
        Assert.Equal(new DropOffCount(1, 0), counts[_second]);
        Assert.Equal(new DropOffCount(1, 0), counts[_third]);
    }

    [Fact]
    public void ACandidateWhoRanOutOfTimeAfterTheFirstQuestion_DropsOffThere()
    {
        var counts = DropOffStatistics.Of([Paper(endedByClock: true, a: true, b: false, c: false)]);

        Assert.Equal(new DropOffCount(1, 1), counts[_first]);
        Assert.False(counts.ContainsKey(_second));
        Assert.False(counts.ContainsKey(_third));
    }

    [Fact]
    public void ACandidateWhoSubmittedEarly_DoesNotDropOff()
    {
        var counts = DropOffStatistics.Of([Paper(endedByClock: false, a: true, b: false, c: false)]);

        Assert.Equal(new DropOffCount(1, 0), counts[_first]);
    }

    [Fact]
    public void ASkippedQuestion_CountsAsReached_WhenALaterOneWasAnswered()
    {
        // The first question was skipped, but the second was answered, so the candidate got as far as the first question and stopped at the second.
        var counts = DropOffStatistics.Of([Paper(endedByClock: true, a: false, b: true, c: false)]);

        Assert.Equal(new DropOffCount(1, 0), counts[_first]);
        Assert.Equal(new DropOffCount(1, 1), counts[_second]);
        Assert.False(counts.ContainsKey(_third));
    }

    [Fact]
    public void Counts_AddUpAcrossCandidates()
    {
        var counts = DropOffStatistics.Of(
        [
            Paper(endedByClock: true, a: true, b: false, c: false),
            Paper(endedByClock: true, a: true, b: false, c: false),
            Paper(endedByClock: false, a: true, b: true, c: false),
        ]);

        Assert.Equal(new DropOffCount(3, 2), counts[_first]);
        Assert.Equal(new DropOffCount(1, 0), counts[_second]);
    }

    [Fact]
    public void TheShare_IsWithheld_UntilTheMinimumCohortReachedTheQuestion()
    {
        Assert.Null(DropOffStatistics.RateOf(new DropOffCount(9, 3), minimumCohortSize: 10));
        Assert.Equal(0.3333m, DropOffStatistics.RateOf(new DropOffCount(12, 4), minimumCohortSize: 10));
    }

    [Fact]
    public void TheShare_IsTheDroppedOffOverTheReached_RoundedToFourPlaces()
    {
        Assert.Equal(0.3333m, DropOffStatistics.RateOf(new DropOffCount(3, 1), minimumCohortSize: 1));
        Assert.Equal(0m, DropOffStatistics.RateOf(new DropOffCount(4, 0), minimumCohortSize: 1));
    }

    [Fact]
    public void TheShare_IsNull_WhenNobodyReachedTheQuestion()
    {
        Assert.Null(DropOffStatistics.RateOf(new DropOffCount(0, 0), minimumCohortSize: 0));
    }
}
