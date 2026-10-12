using ExamPlatform.Modules.Analytics.Application;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Domain;
using ExamPlatform.Modules.Analytics.Domain.Exceptions;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>What an exam's item analysis says, worked from the responses ExamRuntime gives and the question texts the bank holds (FR-37).</summary>
public class ExamItemAnalysisServiceTests
{
    private static readonly DateTime Day = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly Guid _examId = Guid.NewGuid();
    private readonly IExamResponseReader _responses = Substitute.For<IExamResponseReader>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();

    private readonly Guid _q1 = Guid.NewGuid();
    private readonly Guid _q2 = Guid.NewGuid();

    private ExamItemAnalysisService Service(int threshold = 10) => new(_responses, _bank, new ItemAnalysisPolicy(threshold));

    /// <summary>
    /// A candidate who answered both questions, the first correctly if <paramref name="firstRight"/>, the second likewise. The paper carries
    /// <paramref name="maxScore"/> marks; the default of eight is two questions of four marks each. The seconds are the times the page reported
    /// for each question, or none when it reported nothing.
    /// </summary>
    private AttemptResponses Sitting(
        int candidate,
        decimal score,
        bool firstRight,
        bool secondRight,
        DateTime? submitted = null,
        decimal maxScore = 8m,
        int? firstSeconds = null,
        int? secondSeconds = null) =>
        new(
            Guid.NewGuid(),
            Id(candidate),
            submitted ?? Day,
            score,
            maxScore,
            [new QuestionResponse(_q1, firstRight, firstSeconds), new QuestionResponse(_q2, secondRight, secondSeconds)]);

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private void Released(params AttemptResponses[] attempts) =>
        _responses.ReadAsync(_examId, Arg.Any<CancellationToken>()).Returns(
            new ExamResponses(_examId, "Physics", ResultsReleased: true, QuestionIds: [_q1, _q2], Attempts: attempts));

    /// <summary>The bank answers with the texts of whichever of the two questions were asked for.</summary>
    private void QuestionTexts(string first = "Q1 <b>text</b>", string second = "Q2") =>
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var ids = call.Arg<IReadOnlyCollection<Guid>>();
            var found = new List<QuestionSnapshot>();
            if (ids.Contains(_q1))
                found.Add(new QuestionSnapshot(_q1, first, []));
            if (ids.Contains(_q2))
                found.Add(new QuestionSnapshot(_q2, second, []));
            return Task.FromResult<IReadOnlyList<QuestionSnapshot>>(found);
        });

    [Fact]
    public async Task AnUnknownExam_IsReportedAsNotFound_NotAsAnEmptyAnalysis()
    {
        _responses.ReadAsync(_examId, Arg.Any<CancellationToken>()).Returns((ExamResponses?)null);

        await Assert.ThrowsAsync<ExamNotFoundError>(() => Service().GetAsync(_examId, CancellationToken.None));
    }

    [Fact]
    public async Task HeldResults_GiveNoQuestions_AndSayWhy()
    {
        _responses.ReadAsync(_examId, Arg.Any<CancellationToken>()).Returns(
            new ExamResponses(_examId, "Physics", ResultsReleased: false, QuestionIds: [_q1, _q2], Attempts: []));

        var analysis = await Service().GetAsync(_examId, CancellationToken.None);

        Assert.False(analysis.ResultsReleased);
        Assert.Empty(analysis.Questions);
        Assert.Equal(0, analysis.CandidateCount);
    }

    [Fact]
    public async Task ACandidateWhoSatTwice_CountsOnce_ByTheirBestResult()
    {
        // Candidate 1 scored 2, then 5: the 5 is the one that counts, so they are one candidate in the cohort, not two.
        var earlier = Sitting(1, score: 2m, firstRight: false, secondRight: false, submitted: Day);
        var retake = Sitting(1, score: 5m, firstRight: true, secondRight: true, submitted: Day.AddDays(1));
        Released(earlier, retake, Sitting(2, 1m, false, false));
        QuestionTexts();

        var analysis = await Service().GetAsync(_examId, CancellationToken.None);

        Assert.Equal(2, analysis.CandidateCount);
        var first = analysis.Questions[0];
        Assert.Equal(2, first.Attempts);
        Assert.Equal(1, first.CorrectCount);
    }

    [Fact]
    public async Task EachRow_GivesTheQuestionsPlacePreviewAndItsCounts_InTheExamsOrder()
    {
        var sittings = Enumerable.Range(1, 10).Select(i => Sitting(i, i, firstRight: i > 4, secondRight: i > 8)).ToArray();
        Released(sittings);
        QuestionTexts(first: "<p>What is <em>force</em>?</p>");

        var analysis = await Service().GetAsync(_examId, CancellationToken.None);

        Assert.True(analysis.ResultsReleased);
        Assert.Equal(10, analysis.CandidateCount);
        Assert.Equal(3, analysis.GroupSize);
        Assert.Equal<int>([1, 2], analysis.Questions.Select(q => q.Position));
        var first = analysis.Questions[0];
        Assert.Equal("What is force?", first.Text);
        Assert.Equal(10, first.Attempts);
        Assert.Equal(6, first.CorrectCount);
        Assert.Equal(0.6m, first.Difficulty);
        Assert.Equal(2, analysis.Questions[1].CorrectCount);
    }

    [Fact]
    public async Task BelowTheThreshold_TheIndicesAreWithheld_EvenThoughTheCountsAreShown()
    {
        Released(Sitting(1, 2m, true, true), Sitting(2, 1m, false, true));
        QuestionTexts();

        var analysis = await Service(threshold: 10).GetAsync(_examId, CancellationToken.None);

        var first = analysis.Questions[0];
        Assert.Equal(2, first.Attempts);
        Assert.Equal(1, first.CorrectCount);
        Assert.Null(first.Difficulty);
        Assert.Null(first.Discrimination);
        Assert.Equal(10, analysis.MinimumCohortSize);
    }

    [Fact]
    public async Task TheScores_AreEachCandidatesBestResultAsAPercentOfTheirMarks()
    {
        // Candidate 1 scored 2 and then 5 of 8, so the retake counts at 62.5%. Candidate 2 scored all 8 and candidate 3 none.
        Released(
            Sitting(1, 2m, false, false, submitted: Day),
            Sitting(1, 5m, true, false, submitted: Day.AddDays(1)),
            Sitting(2, 8m, true, true),
            Sitting(3, 0m, false, false));
        QuestionTexts();

        var scores = (await Service().GetAsync(_examId, CancellationToken.None)).Scores;

        Assert.Equal(3, scores.Count);
        Assert.Equal(54.17m, scores.MeanPercentOfMarks);
        Assert.Equal(62.5m, scores.MedianPercentOfMarks);
        Assert.Equal(1, scores.Distribution[0].Count);
        Assert.Equal(1, scores.Distribution[6].Count);
        Assert.Equal(1, scores.Distribution[9].Count);
    }

    [Fact]
    public async Task ACandidateWhosePaperHadNoMarks_IsCountedButLeftOutOfTheScores()
    {
        // A paper with no marks to earn has no percentage to place, so it cannot sit in the scores; it still counts as a candidate.
        Released(Sitting(1, 0m, false, false, maxScore: 0m), Sitting(2, 4m, true, false));
        QuestionTexts();

        var analysis = await Service().GetAsync(_examId, CancellationToken.None);

        Assert.Equal(2, analysis.CandidateCount);
        Assert.Equal(1, analysis.Scores.Count);
        Assert.Equal(50m, analysis.Scores.MeanPercentOfMarks);
    }

    [Fact]
    public async Task WhileTheResultsAreHeld_TheScoresAreEmpty()
    {
        _responses.ReadAsync(_examId, Arg.Any<CancellationToken>()).Returns(
            new ExamResponses(_examId, "Physics", ResultsReleased: false, QuestionIds: [_q1, _q2], Attempts: []));

        var scores = (await Service().GetAsync(_examId, CancellationToken.None)).Scores;

        Assert.Equal(0, scores.Count);
        Assert.Null(scores.MeanPercentOfMarks);
        Assert.Null(scores.MedianPercentOfMarks);
        Assert.Empty(scores.Distribution);
    }

    [Fact]
    public async Task AReleasedExamNobodySatYet_HasNoMeanOrMedian_ButShowsEveryBand()
    {
        Released();
        QuestionTexts();

        var scores = (await Service().GetAsync(_examId, CancellationToken.None)).Scores;

        Assert.Equal(0, scores.Count);
        Assert.Null(scores.MeanPercentOfMarks);
        Assert.Null(scores.MedianPercentOfMarks);
        Assert.Equal(ScoreDistribution.BandCount, scores.Distribution.Count);
        Assert.All(scores.Distribution, band => Assert.Equal(0, band.Count));
    }

    [Fact]
    public async Task AQuestionTheBankCannotReturn_IsAFault_NotABlankRow()
    {
        Released(Sitting(1, 2m, true, true));
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([new QuestionSnapshot(_q1, "Q1", [])]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().GetAsync(_examId, CancellationToken.None));
    }

    [Fact]
    public async Task BelowTheMinimumCohort_TheAverageTimeIsWithheld_EvenThoughTheCountIsShown()
    {
        Released(Sitting(1, 2m, true, true, firstSeconds: 30), Sitting(2, 1m, false, false, firstSeconds: 50));
        QuestionTexts();

        var time = (await Service(threshold: 10).GetAsync(_examId, CancellationToken.None)).Questions[0].Time;

        Assert.Equal(2, time.CandidatesTimed);
        Assert.Null(time.AverageSeconds);
        Assert.Equal(ItemTimeStatus.BelowMinimumCohort, time.Status);
    }

    [Fact]
    public async Task AtTheMinimumCohort_TheAverageTimeIsShown()
    {
        // Ten candidates timed at 10, 20 ... 100 seconds: the average is 55 and the question's figure is shown.
        Released(Enumerable.Range(1, 10).Select(i => Sitting(i, i, true, true, firstSeconds: i * 10)).ToArray());
        QuestionTexts();

        var time = (await Service(threshold: 10).GetAsync(_examId, CancellationToken.None)).Questions[0].Time;

        Assert.Equal(10, time.CandidatesTimed);
        Assert.Equal(55m, time.AverageSeconds);
        Assert.Equal(ItemTimeStatus.Shown, time.Status);
    }

    [Fact]
    public async Task AQuestionNobodyWasTimedOn_HasNoAverage_AndSaysItHasNoTimes()
    {
        Released(Sitting(1, 2m, true, true), Sitting(2, 1m, false, false));
        QuestionTexts();

        var time = (await Service().GetAsync(_examId, CancellationToken.None)).Questions[0].Time;

        Assert.Equal(0, time.CandidatesTimed);
        Assert.Null(time.AverageSeconds);
        Assert.Equal(ItemTimeStatus.NoTimes, time.Status);
    }

    [Fact]
    public async Task TheExamAverage_IsOverItsTimedAnswersOnly_NotEveryAnswer()
    {
        // The timed answers are 10, 30 and 20. The third candidate has no times, so the average is 60 over 3 timed answers, not over all six.
        Released(
            Sitting(1, 2m, true, true, firstSeconds: 10, secondSeconds: 30),
            Sitting(2, 1m, false, false, firstSeconds: 20),
            Sitting(3, 0m, false, false));
        QuestionTexts();

        var time = (await Service().GetAsync(_examId, CancellationToken.None)).Time;

        Assert.Equal(3, time.QuestionsTimed);
        Assert.Equal(20m, time.AverageSeconds);
    }

    [Fact]
    public async Task ACandidateWhoSatTwice_IsTimedByTheirBestResultOnly()
    {
        // Candidate 1's retake is the best result, so the 90 seconds of the earlier sitting is left out: the timed answers are 10 and 20.
        Released(
            Sitting(1, 2m, false, false, submitted: Day, firstSeconds: 90),
            Sitting(1, 5m, true, true, submitted: Day.AddDays(1), firstSeconds: 10),
            Sitting(2, 1m, false, false, firstSeconds: 20));
        QuestionTexts();

        var analysis = await Service(threshold: 2).GetAsync(_examId, CancellationToken.None);

        Assert.Equal(2, analysis.Questions[0].Time.CandidatesTimed);
        Assert.Equal(15m, analysis.Questions[0].Time.AverageSeconds);
        Assert.Equal(2, analysis.Time.QuestionsTimed);
        Assert.Equal(15m, analysis.Time.AverageSeconds);
    }

    [Fact]
    public async Task WhileTheResultsAreHeld_NoTimeIsCounted_EvenIfTheAttemptsCarryOne()
    {
        // The held results are not read at all, so a reported time in them must not reach the figures.
        _responses.ReadAsync(_examId, Arg.Any<CancellationToken>()).Returns(
            new ExamResponses(_examId, "Physics", ResultsReleased: false, QuestionIds: [_q1, _q2], Attempts: [Sitting(1, 2m, true, true, firstSeconds: 30)]));

        var analysis = await Service().GetAsync(_examId, CancellationToken.None);

        Assert.Equal(0, analysis.CandidateCount);
        Assert.Equal(0, analysis.Time.QuestionsTimed);
        Assert.Null(analysis.Time.AverageSeconds);
    }
}
