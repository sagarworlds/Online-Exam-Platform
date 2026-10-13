using System.Text.Json;
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

    /// <summary>The item analysis threshold the host ships with (FR-37), which the comparison reuses.</summary>
    private const int MinimumCohortSize = 30;

    private readonly ICandidateResultReader _results = Substitute.For<ICandidateResultReader>();
    private readonly IQuestionTopics _topics = Substitute.For<IQuestionTopics>();
    private readonly IExamResponseReader _responses = Substitute.For<IExamResponseReader>();
    private readonly Dictionary<Guid, IReadOnlyList<string>> _labels = [];
    private readonly CandidateAnalyticsService _service;

    public CandidateAnalyticsServiceTests()
    {
        _topics.GetTopicsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<string>>>(_labels));
        _service = new CandidateAnalyticsService(_results, _topics, _responses, new ItemAnalysisPolicy(MinimumCohortSize));
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

        Assert.Equal(3, dto.Time!.QuestionsTimed);
        Assert.Equal(60m, dto.Time!.AverageSeconds);
        Assert.Equal(["Maths", "Physics"], dto.Time!.Sections.Select(s => s.Name));
        Assert.Equal(45m, dto.Time!.Sections[0].AverageSeconds);
        Assert.Equal(90m, dto.Time!.Sections[1].AverageSeconds);
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

        Assert.Equal(1, dto.Time!.QuestionsTimed);
        Assert.Equal(40m, dto.Time!.AverageSeconds);
    }

    [Fact]
    public async Task WithNoTimedQuestion_TheTimeFiguresAreEmpty_NotZero()
    {
        Released(Result("Mock", Day1, 0m, 0m, Section("Maths", correct: 0, wrong: 0)));

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, dto.Time!.QuestionsTimed);
        Assert.Null(dto.Time!.AverageSeconds);
        Assert.Empty(dto.Time!.Sections);
    }

    [Fact]
    public async Task WhenEveryResultsTimeIsWithheld_ThereIsNoTimeFigure_NotAZero()
    {
        // A candidate under 18 while the switch is off: the time is withheld, so the figures are absent (null), not a count of zero.
        var section = Section("Maths", 1, 0);
        var sectionId = Guid.NewGuid();
        var withheld = WithQuestions(
            Result("Mock", Day1, 4, 4, section) with { TimeWithheld = true },
            Question(sectionId, CandidateQuestionOutcome.Correct));
        Released(withheld);

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(dto.Time);
        Assert.Equal(1, dto.ResultCount);
        Assert.Single(dto.Sections);
    }

    [Fact]
    public async Task WithNoReleasedResult_NoTimeFigureIsShown_NotAZero()
    {
        // A candidate under 18 with nothing released has no time to show either, so the time is absent rather than a count of zero.
        Released();

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(dto.Time);
    }

    [Fact]
    public async Task WhenSomeResultsTimeIsWithheld_TheFiguresComeFromTheRestOnly()
    {
        var sectionId = Guid.NewGuid();
        var withheld = WithQuestions(
            Result("Mock 1", Day1, 4, 4, Section("Maths", 1, 0, id: sectionId)) with { TimeWithheld = true },
            Question(sectionId, CandidateQuestionOutcome.Correct));
        var kept = WithQuestions(
            Result("Mock 2", Day1.AddDays(7), 2, 4, Section("Maths", 1, 1, id: sectionId)),
            Question(sectionId, CandidateQuestionOutcome.Correct, 40));
        Released(withheld, kept);

        var dto = await _service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(dto.Time);
        Assert.Equal(1, dto.Time!.QuestionsTimed);
        Assert.Equal(40m, dto.Time!.AverageSeconds);
    }

    private static CandidateResult ResultAt(Guid examId, string examName, decimal score, decimal maxScore, DateTime? submittedAt = null) =>
        new(Guid.NewGuid(), examId, examName, submittedAt ?? Day1, score, maxScore, []);

    private static AttemptResponses Sitting(Guid candidate, decimal score, decimal maxScore = 100m) =>
        new(Guid.NewGuid(), candidate, Day1, EndedByClock: false, score, maxScore, []);

    /// <summary>The candidate's own sitting followed by <paramref name="others"/> other candidates, each on a paper of 100 marks.</summary>
    private static List<AttemptResponses> CohortWith(Guid candidate, decimal ownScore, int others, decimal othersScore)
    {
        var cohort = new List<AttemptResponses> { Sitting(candidate, ownScore) };
        cohort.AddRange(Enumerable.Range(0, others).Select(_ => Sitting(Guid.NewGuid(), othersScore)));
        return cohort;
    }

    /// <summary>Gives the exam's cohort as the reader does: its attempts only when released, and none otherwise.</summary>
    private void CohortOf(Guid examId, bool released, IReadOnlyList<AttemptResponses> attempts) =>
        _responses.ReadAsync(examId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ExamResponses?>(
            new ExamResponses(examId, "Exam", released, [], attempts)));

    [Fact]
    public async Task ACohortBelowThirty_ShowsNoTopperAndNoAverage_AndSaysSo()
    {
        var examId = Guid.NewGuid();
        var me = Guid.NewGuid();
        Released(ResultAt(examId, "Mock", 60m, 100m));
        CohortOf(examId, released: true, CohortWith(me, 60m, others: 28, othersScore: 80m));

        var comparison = Assert.Single((await _service.GetAsync(me, CancellationToken.None)).Comparisons);

        Assert.Equal(ExamComparisonStatus.BelowMinimum, comparison.Status);
        Assert.Null(comparison.CohortSize);
        Assert.Null(comparison.TopperPercentOfMarks);
        Assert.Null(comparison.AveragePercentOfMarks);
        Assert.Equal(MinimumCohortSize, comparison.MinimumCohortSize);
        Assert.Equal(60m, comparison.CandidatePercentOfMarks);
    }

    [Fact]
    public async Task ACohortOfThirty_ShowsTheTopperAndTheAverage()
    {
        var examId = Guid.NewGuid();
        var me = Guid.NewGuid();
        Released(ResultAt(examId, "Mock", 60m, 100m));
        CohortOf(examId, released: true, CohortWith(me, 60m, others: 29, othersScore: 80m));

        var comparison = Assert.Single((await _service.GetAsync(me, CancellationToken.None)).Comparisons);

        Assert.Equal(ExamComparisonStatus.Shown, comparison.Status);
        Assert.Equal(30, comparison.CohortSize);
        Assert.Equal(80m, comparison.TopperPercentOfMarks);
        Assert.Equal(79.33m, comparison.AveragePercentOfMarks);
    }

    [Fact]
    public async Task TheCandidatesOwnResult_IsInTheCohort_SoItCountsInTheAverage()
    {
        // Thirty sat the exam: the candidate at 100% and 29 others at 40%. Left out, the average would be 40 and the topper 40; counted, 42 and 100.
        var examId = Guid.NewGuid();
        var me = Guid.NewGuid();
        Released(ResultAt(examId, "Mock", 100m, 100m));
        CohortOf(examId, released: true, CohortWith(me, 100m, others: 29, othersScore: 40m));

        var comparison = Assert.Single((await _service.GetAsync(me, CancellationToken.None)).Comparisons);

        Assert.Equal(100m, comparison.CandidatePercentOfMarks);
        Assert.Equal(100m, comparison.TopperPercentOfMarks);
        Assert.Equal(42m, comparison.AveragePercentOfMarks);
    }

    [Fact]
    public async Task AnExamWhoseResultsAreHeld_ContributesNoCohort_EvenIfItsAttemptsWereRead()
    {
        // The reader holds the results, so no attempt may count, even one that a faulty reader handed over: thirty attempts must not make a cohort.
        var examId = Guid.NewGuid();
        var me = Guid.NewGuid();
        Released(ResultAt(examId, "Mock", 60m, 100m));
        CohortOf(examId, released: false, CohortWith(me, 60m, others: 29, othersScore: 80m));

        var comparison = Assert.Single((await _service.GetAsync(me, CancellationToken.None)).Comparisons);

        Assert.Equal(ExamComparisonStatus.BelowMinimum, comparison.Status);
        Assert.Null(comparison.CohortSize);
        Assert.Null(comparison.TopperPercentOfMarks);
        Assert.Null(comparison.AveragePercentOfMarks);
    }

    [Fact]
    public async Task ACandidatesUnreleasedResult_IsNotCompared_AndItsExamIsNotReadForACohort()
    {
        // The reader gives only the released result. The held exam is never in the list, so no entry is made for it and its cohort is never read.
        var released = Guid.NewGuid();
        var held = Guid.NewGuid();
        var me = Guid.NewGuid();
        Released(ResultAt(released, "Open", 50m, 100m));
        CohortOf(released, released: true, CohortWith(me, 50m, others: 0, othersScore: 0m));

        var comparisons = (await _service.GetAsync(me, CancellationToken.None)).Comparisons;

        Assert.Equal([released], comparisons.Select(c => c.ExamId));
        await _responses.DidNotReceive().ReadAsync(held, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACandidateWhoseHighestRawScoreIsNotTheirHighestPercent_IsComparedOnThePercent()
    {
        // The candidate sat one exam on two papers: 6 of 8 (75%) and 20 of 40 (50%). Their best result is the 75%, so their own percentage is 75.
        // The cohort is them and 29 others at 50%: the topper is 75 and the average is (75 + 29 x 50) / 30 = 50.83.
        var examId = Guid.NewGuid();
        var me = Guid.NewGuid();
        Released(
            ResultAt(examId, "Mock", 6m, 8m, Day1),
            ResultAt(examId, "Mock", 20m, 40m, Day1.AddDays(1)));
        var cohort = new List<AttemptResponses> { Sitting(me, 6m, 8m), Sitting(me, 20m, 40m) };
        cohort.AddRange(Enumerable.Range(0, 29).Select(_ => Sitting(Guid.NewGuid(), 50m)));
        CohortOf(examId, released: true, cohort);

        var comparison = Assert.Single((await _service.GetAsync(me, CancellationToken.None)).Comparisons);

        Assert.Equal(75m, comparison.CandidatePercentOfMarks);
        Assert.Equal(30, comparison.CohortSize);
        Assert.Equal(75m, comparison.TopperPercentOfMarks);
        Assert.Equal(50.83m, comparison.AveragePercentOfMarks);
    }

    [Fact]
    public async Task TheComparisons_HaveOneEntryPerExam_OldestExamFirst_OnItsBestResult()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Released(
            ResultAt(second, "Later", 70m, 100m, Day1.AddDays(5)),
            ResultAt(first, "Earlier", 50m, 100m, Day1),
            ResultAt(first, "Earlier", 90m, 100m, Day1.AddDays(2)));

        var comparisons = (await _service.GetAsync(Guid.NewGuid(), CancellationToken.None)).Comparisons;

        Assert.Equal([first, second], comparisons.Select(c => c.ExamId));
        Assert.Equal(90m, comparisons[0].CandidatePercentOfMarks);
        Assert.Equal(70m, comparisons[1].CandidatePercentOfMarks);
    }

    [Fact]
    public async Task TheThreshold_IsTheItemAnalysisPolicy_SoAPilotSettingMovesBothRulesTogether()
    {
        var examId = Guid.NewGuid();
        var me = Guid.NewGuid();
        var service = new CandidateAnalyticsService(_results, _topics, _responses, new ItemAnalysisPolicy(10));
        Released(ResultAt(examId, "Mock", 60m, 100m));
        CohortOf(examId, released: true, CohortWith(me, 60m, others: 9, othersScore: 80m));

        var comparison = Assert.Single((await service.GetAsync(me, CancellationToken.None)).Comparisons);

        Assert.Equal(ExamComparisonStatus.Shown, comparison.Status);
        Assert.Equal(10, comparison.MinimumCohortSize);
        Assert.Equal(10, comparison.CohortSize);
    }

    [Fact]
    public async Task TheComparisonCarriesNoOtherCandidatesIdentity()
    {
        // Thirty candidates, each with an id the output must not show. The figures are the only thing that may leave the service.
        var examId = Guid.NewGuid();
        var me = Guid.NewGuid();
        var cohort = CohortWith(me, 60m, others: 29, othersScore: 80m);
        Released(ResultAt(examId, "Mock", 60m, 100m));
        CohortOf(examId, released: true, cohort);

        var comparisons = (await _service.GetAsync(me, CancellationToken.None)).Comparisons;
        var json = JsonSerializer.Serialize(comparisons);

        Assert.DoesNotContain(me.ToString(), json);
        Assert.All(cohort, sitting => Assert.DoesNotContain(sitting.CandidateId.ToString(), json));
    }
}
