using ExamPlatform.Modules.Analytics.Application;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>What a candidate's analytics say, worked from the released results ExamRuntime gives (FR-36).</summary>
public class CandidateAnalyticsServiceTests
{
    private static readonly DateTime Day1 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly ICandidateResultReader _results = Substitute.For<ICandidateResultReader>();
    private readonly CandidateAnalyticsService _service;

    public CandidateAnalyticsServiceTests()
    {
        _service = new CandidateAnalyticsService(_results);
    }

    private static CandidateSectionResult Section(string name, int correct, int wrong, int partial = 0, int unanswered = 0) =>
        new(Guid.NewGuid(), name, Score: correct, CorrectCount: correct, WrongCount: wrong, PartialCount: partial, UnansweredCount: unanswered);

    private static CandidateResult Result(string examName, DateTime submittedAt, decimal score, decimal maxScore, params CandidateSectionResult[] sections) =>
        new(Guid.NewGuid(), Guid.NewGuid(), examName, submittedAt, score, maxScore, sections);

    private void Released(params CandidateResult[] results) =>
        _results.ListReleasedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CandidateResult>>(results));

    [Fact]
    public async Task ReadsOnlyTheNamedCandidate_SoNoOneElsesResultsAreFetched()
    {
        var candidate = Guid.NewGuid();
        Released();

        await _service.GetAsync(candidate, CancellationToken.None);

        await _results.Received(1).ListReleasedAsync(candidate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoReleasedResult_TheTrendAndSectionsAreEmpty_NotAnError()
    {
        Released();

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, dto.ResultCount);
        Assert.Empty(dto.Trend);
        Assert.Empty(dto.Sections);
    }

    [Fact]
    public async Task TheTrend_IsInTheOrderTheExamsWereSat_WhateverOrderTheReaderGaveTheResults()
    {
        var later = Result("Mock 2", Day1.AddDays(7), 60m, 80m);
        var earlier = Result("Mock 1", Day1, 20m, 80m);
        Released(later, earlier);

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(["Mock 1", "Mock 2"], dto.Trend.Select(p => p.ExamName));
        Assert.Equal<decimal?>([25m, 75m], dto.Trend.Select(p => p.PercentOfMarks));
    }

    [Fact]
    public async Task ATrendPoint_IsNullPercentWhenNoMarksWereAvailable()
    {
        Released(Result("Unmarked", Day1, 0m, 0m));

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(Assert.Single(dto.Trend).PercentOfMarks);
    }

    [Fact]
    public async Task TheSameSectionInTwoExams_IsOneSubject_IgnoringCaseAndSpaces()
    {
        Released(
            Result("Mock 1", Day1, 4m, 8m, Section("Physics", correct: 2, wrong: 1)),
            Result("Mock 2", Day1.AddDays(3), 2m, 8m, Section(" physics ", correct: 1, wrong: 1, unanswered: 2)));

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        var physics = Assert.Single(dto.Sections);
        Assert.Equal("Physics", physics.Name);
        Assert.Equal(2, physics.ResultCount);
        Assert.Equal(3, physics.CorrectCount);
        Assert.Equal(2, physics.WrongCount);
        Assert.Equal(2, physics.UnansweredCount);
        Assert.Equal(60m, physics.Accuracy);
    }

    [Fact]
    public async Task TheSections_AreListedWeakestAccuracyFirst()
    {
        Released(Result("Mock", Day1, 0m, 0m,
            Section("Strong", correct: 4, wrong: 0),
            Section("Weak", correct: 1, wrong: 3),
            Section("Blank", correct: 0, wrong: 0, unanswered: 5)));

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        // The section nobody answered has no accuracy at all, so it goes last rather than being mistaken for the weakest.
        Assert.Equal(["Weak", "Strong", "Blank"], dto.Sections.Select(s => s.Name));
        Assert.Null(dto.Sections[2].Accuracy);
    }

    [Fact]
    public async Task ResultCount_IsTheNumberOfReleasedResults()
    {
        Released(Result("A", Day1, 1m, 2m), Result("B", Day1.AddDays(1), 1m, 2m));

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, dto.ResultCount);
    }
}
