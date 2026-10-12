using ExamPlatform.Modules.Analytics.Application;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>What a candidate's analytics say, worked from the released results ExamRuntime gives (FR-36).</summary>
public class CandidateAnalyticsServiceTests
{
    private static readonly DateTime Day1 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly ICandidateResultReader _results = Substitute.For<ICandidateResultReader>();
    private readonly IQuestionTopics _topics = Substitute.For<IQuestionTopics>();
    private readonly Dictionary<Guid, IReadOnlyList<string>> _labels = [];
    private readonly CandidateAnalyticsService _service;

    public CandidateAnalyticsServiceTests()
    {
        _topics.GetTopicsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<string>>>(_labels));
        _service = new CandidateAnalyticsService(_results, _topics);
    }

    private static CandidateSectionResult Section(string name, int correct, int wrong, int partial = 0, int unanswered = 0, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), name, Score: correct, CorrectCount: correct, WrongCount: wrong, PartialCount: partial, UnansweredCount: unanswered);

    private static CandidateResult Result(string examName, DateTime submittedAt, decimal score, decimal maxScore, params CandidateSectionResult[] sections) =>
        new(Guid.NewGuid(), Guid.NewGuid(), examName, submittedAt, score, maxScore, sections);

    private static CandidateQuestionResult Question(Guid sectionId, CandidateQuestionOutcome outcome, int? seconds = null) =>
        new(Guid.NewGuid(), sectionId, outcome, seconds);

    private static CandidateResult WithQuestions(CandidateResult result, params CandidateQuestionResult[] questions) =>
        result with { Questions = questions };

    private void Labelled(CandidateQuestionResult question, params string[] topics) => _labels[question.QuestionId] = topics;

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

    [Fact]
    public async Task AccuracyByTopic_GroupsEachAnswerUnderItsTopic_WeakestFirst()
    {
        var maths = Guid.NewGuid();
        var algebraRight = Question(maths, CandidateQuestionOutcome.Correct);
        var algebraWrong = Question(maths, CandidateQuestionOutcome.Wrong);
        var graphsRight = Question(maths, CandidateQuestionOutcome.Correct);
        Labelled(algebraRight, "algebra");
        Labelled(algebraWrong, "algebra");
        Labelled(graphsRight, "graphs");
        Released(WithQuestions(Result("Mock", Day1, 0m, 0m, Section("Maths", correct: 2, wrong: 1, id: maths)), algebraRight, algebraWrong, graphsRight));

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(["algebra", "graphs"], dto.Topics.Select(t => t.Name));
        var algebra = dto.Topics[0];
        Assert.Equal(2, algebra.QuestionCount);
        Assert.Equal(1, algebra.CorrectCount);
        Assert.Equal(1, algebra.WrongCount);
        Assert.Equal(50m, algebra.Accuracy);
        Assert.Equal(100m, dto.Topics[1].Accuracy);
    }

    [Fact]
    public async Task AQuestionWithTwoTopics_CountsUnderBoth()
    {
        var sectionId = Guid.NewGuid();
        var question = Question(sectionId, CandidateQuestionOutcome.Partial);
        Labelled(question, "algebra", "graphs");
        Released(WithQuestions(Result("Mock", Day1, 0m, 0m, Section("Maths", correct: 0, wrong: 0, partial: 1)), question));

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(["algebra", "graphs"], dto.Topics.Select(t => t.Name));
        Assert.All(dto.Topics, t => Assert.Equal(1, t.PartialCount));
    }

    [Fact]
    public async Task AQuestionWithNoTopic_IsLeftOutOfTheTopicBreakdown()
    {
        var sectionId = Guid.NewGuid();
        var unlabelled = Question(sectionId, CandidateQuestionOutcome.Wrong);
        Released(WithQuestions(Result("Mock", Day1, 0m, 0m, Section("Maths", correct: 0, wrong: 1)), unlabelled));

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(dto.Topics);
    }

    [Fact]
    public async Task TheTopicsAreReadForTheQuestionsOfTheReleasedResults_NotAnyOthers()
    {
        var sectionId = Guid.NewGuid();
        var first = Question(sectionId, CandidateQuestionOutcome.Correct);
        var second = Question(sectionId, CandidateQuestionOutcome.Correct);
        Released(
            WithQuestions(Result("Mock 1", Day1, 0m, 0m, Section("Maths", correct: 1, wrong: 0)), first),
            WithQuestions(Result("Mock 2", Day1.AddDays(1), 0m, 0m, Section("Maths", correct: 1, wrong: 0)), second));

        await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        await _topics.Received(1).GetTopicsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(first.QuestionId) && ids.Contains(second.QuestionId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TimePerQuestion_AveragesTheReportedQuestions_OverallAndBySection()
    {
        var maths = Guid.NewGuid();
        var physics = Guid.NewGuid();
        Released(WithQuestions(
            Result("Mock", Day1, 0m, 0m, Section("Maths", correct: 0, wrong: 0, id: maths), Section("Physics", correct: 0, wrong: 0, id: physics)),
            Question(maths, CandidateQuestionOutcome.Correct, seconds: 60),
            Question(maths, CandidateQuestionOutcome.Wrong, seconds: 30),
            Question(physics, CandidateQuestionOutcome.Unanswered, seconds: 90)));

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(3, dto.Time.QuestionsTimed);
        Assert.Equal(60m, dto.Time.AverageSeconds);
        Assert.Equal(["Maths", "Physics"], dto.Time.Sections.Select(s => s.Name));
        Assert.Equal(45m, dto.Time.Sections[0].AverageSeconds);
        Assert.Equal(90m, dto.Time.Sections[1].AverageSeconds);
    }

    [Fact]
    public async Task AQuestionNeverReported_IsNotCountedAsZeroSeconds()
    {
        var section = Guid.NewGuid();
        Released(WithQuestions(
            Result("Mock", Day1, 0m, 0m, Section("Maths", correct: 0, wrong: 0, id: section)),
            Question(section, CandidateQuestionOutcome.Correct, seconds: 40),
            Question(section, CandidateQuestionOutcome.Correct, seconds: null)));

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(1, dto.Time.QuestionsTimed);
        Assert.Equal(40m, dto.Time.AverageSeconds);
    }

    [Fact]
    public async Task WithNoTimedQuestion_TheTimeFiguresAreEmpty_NotZero()
    {
        Released(Result("Mock", Day1, 0m, 0m, Section("Maths", correct: 0, wrong: 0)));

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, dto.Time.QuestionsTimed);
        Assert.Null(dto.Time.AverageSeconds);
        Assert.Empty(dto.Time.Sections);
    }
}
