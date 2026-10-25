using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// A typed answer is marked under the rule of the question version the candidate sat (FR-5): a lenient question forgives a stray full stop or
/// an accent, an exact one does not, and a question's rule never leaks onto another.
/// </summary>
public class TextMatchRuleMarkingTests
{
    private static ExamSnapshot ExamOf(QuestionSnapshot question) => Fixtures.Exam([question], correct: 2m, incorrect: -1m, unattempted: 0m);

    private static QuestionMark Mark(QuestionSnapshot question, string typed) => AttemptScorer.Mark(ExamOf(question), question, Array.Empty<Guid>(), typed);

    [Theory]
    [InlineData("Paris.")]
    [InlineData("  paris! ")]
    [InlineData("\"Paris\"")]
    [InlineData("City-of-Paris")]
    public void ALenientQuestion_ForgivesStrayPunctuation(string typed) =>
        Assert.Equal(new QuestionMark(AnswerVerdict.Correct, 2m), Mark(Fixtures.TextQuestion(TextMatchRule.Lenient, "Paris", "City of Paris"), typed));

    [Fact]
    public void ALenientQuestion_ForgivesAnAccent() =>
        Assert.Equal(new QuestionMark(AnswerVerdict.Correct, 2m), Mark(Fixtures.TextQuestion(TextMatchRule.Lenient, "Zürich"), "zurich"));

    [Theory]
    [InlineData("Paris.")]
    [InlineData("City-of-Paris")]
    public void AnExactQuestion_DoesNotForgiveThem_ExactlyAsBefore(string typed) =>
        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark(Fixtures.TextQuestion(TextMatchRule.Exact, "Paris", "City of Paris"), typed));

    [Fact]
    public void ALenientQuestion_StillMarksADifferentAnswerWrong() =>
        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark(Fixtures.TextQuestion(TextMatchRule.Lenient, "Paris"), "Lyon."));

    [Fact]
    public void ALenientQuestion_StillKeepsASignThatChangesTheAnswer() =>
        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark(Fixtures.TextQuestion(TextMatchRule.Lenient, "x+1"), "x-1"));

    [Fact]
    public void AnAnswerThatIsOnlyPunctuation_IsWrongNotUnansweredAndNeverRight() =>
        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark(Fixtures.TextQuestion(TextMatchRule.Lenient, "?"), "..."));

    [Fact]
    public void TheScore_UsesEachQuestionsOwnRule()
    {
        var lenient = Fixtures.TextQuestion(TextMatchRule.Lenient, "Paris");
        var exact = Fixtures.TextQuestion(TextMatchRule.Exact, "Paris");
        var exam = Fixtures.Exam([lenient, exact], correct: 2m, incorrect: -1m, unattempted: 0m);
        var attempt = Attempt.Start(exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(lenient.Id, "Paris.", Fixtures.Now);
        attempt.RecordTextAnswer(exact.Id, "Paris.", Fixtures.Now);

        var score = AttemptScorer.Score(
            exam, new Dictionary<Guid, QuestionSnapshot> { [lenient.Id] = lenient, [exact.Id] = exact }, attempt.Answers.ToList());

        Assert.Equal(new AttemptScore(1m, 4m), score);
    }
}

/// <summary>An answer a question's rule already accepts is right without being asked, so the lenient rule also saves calls to the AI evaluator.</summary>
public class TextMatchRuleAiSelectionTests
{
    private static Attempt Open() => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));

    [Fact]
    public void AnAnswerTheLenientRuleAccepts_IsNotSentToTheEvaluator()
    {
        var capital = Fixtures.TextQuestion(TextMatchRule.Lenient, "Paris");
        var attempt = Open();
        attempt.RecordTextAnswer(capital.Id, "Paris.", Fixtures.Now);

        Assert.Empty(TypedAnswerChecks.QuestionsToCheck(attempt, new Dictionary<Guid, QuestionSnapshot> { [capital.Id] = capital }));
    }

    [Fact]
    public void TheSameAnswerUnderTheExactRule_IsStillSentToTheEvaluator()
    {
        var capital = Fixtures.TextQuestion(TextMatchRule.Exact, "Paris");
        var attempt = Open();
        attempt.RecordTextAnswer(capital.Id, "Paris.", Fixtures.Now);

        Assert.Equal([capital.Id], TypedAnswerChecks.QuestionsToCheck(attempt, new Dictionary<Guid, QuestionSnapshot> { [capital.Id] = capital }));
    }
}

/// <summary>A question's difficulty figures count a typed answer as the version it was given against judged it, rule included.</summary>
public class TextMatchRuleStatisticsTests
{
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();

    [Fact]
    public async Task EachAnswerIsJudgedByTheRuleOfTheVersionItSat()
    {
        // Version 1 was exact, and a correction made version 2 lenient. "Paris." was wrong when sat against version 1 and right against 2.
        var first = Fixtures.TextQuestion(TextMatchRule.Exact, "Paris");
        var second = first with { VersionNumber = 2, MatchRule = TextMatchRule.Lenient };
        first = first with { VersionNumber = 1 };
        _attempts.ListSubmittedAnswersAsync(first.Id, Arg.Any<CancellationToken>()).Returns(
        [
            new SubmittedAnswer([], 1, "Paris."),
            new SubmittedAnswer([], 2, "Paris."),
            new SubmittedAnswer([], 2, "Lyon."),
        ]);
        _bank.GetVersionsAsync(Arg.Any<IReadOnlyCollection<QuestionVersionRef>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([first, second]));
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([second]));

        var stats = await new AttemptAnswerStatisticsSource(_attempts, _bank).ReadAsync(first.Id, CancellationToken.None);

        Assert.Equal((3, 1), (stats.Answered, stats.Correct));
    }
}
