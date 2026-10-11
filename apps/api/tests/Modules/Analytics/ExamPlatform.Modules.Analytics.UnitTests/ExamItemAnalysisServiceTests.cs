using ExamPlatform.Modules.Analytics.Application;
using ExamPlatform.Modules.Analytics.Contracts;
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

    /// <summary>A candidate who answered both questions, the first correctly if <paramref name="firstRight"/>, the second likewise.</summary>
    private AttemptResponses Sitting(int candidate, decimal score, bool firstRight, bool secondRight, DateTime? submitted = null) =>
        new(
            Guid.NewGuid(),
            Id(candidate),
            submitted ?? Day,
            score,
            [new QuestionResponse(_q1, firstRight), new QuestionResponse(_q2, secondRight)]);

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
    public async Task AQuestionTheBankCannotReturn_IsAFault_NotABlankRow()
    {
        Released(Sitting(1, 2m, true, true));
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([new QuestionSnapshot(_q1, "Q1", [])]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().GetAsync(_examId, CancellationToken.None));
    }
}
