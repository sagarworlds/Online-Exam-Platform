using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>How candidates have answered a question (FR-9): counted per attempt, against the question as that attempt sat it.</summary>
public class AttemptAnswerStatisticsSourceTests
{
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly QuestionSnapshot _question = Fixtures.Question("Capital of France?");

    private AttemptAnswerStatisticsSource Source => new(_attempts, _bank);

    private void Answers(params SubmittedAnswer[] answers) =>
        _attempts.ListSubmittedAnswersAsync(_question.Id, Arg.Any<CancellationToken>()).Returns(answers);

    private void BankHasVersions(params QuestionSnapshot[] versions)
    {
        _bank.GetVersionsAsync(Arg.Any<IReadOnlyCollection<QuestionVersionRef>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>(versions));
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([_question]));
    }

    [Fact]
    public async Task NobodyHasAnswered_IsAllZeros()
    {
        Answers();

        var stats = await Source.ReadAsync(_question.Id, CancellationToken.None);

        Assert.Equal((0, 0), (stats.Answered, stats.Correct));
        Assert.Empty(stats.Chosen);
    }

    [Fact]
    public async Task EachAnswerIsCounted_AndOnlyTheOnesThatChoseExactlyTheCorrectOptionsAreCorrect()
    {
        var wrong = _question.Options.First(o => !o.IsCorrect).Id;
        BankHasVersions(_question);
        Answers(new SubmittedAnswer([_question.Correct()], 1), new SubmittedAnswer([_question.Correct()], 1), new SubmittedAnswer([wrong], 1));

        var stats = await Source.ReadAsync(_question.Id, CancellationToken.None);

        Assert.Equal((3, 2), (stats.Answered, stats.Correct));
        Assert.Equal(2, stats.Chosen[_question.Correct()]);
        Assert.Equal(1, stats.Chosen[wrong]);
    }

    [Fact]
    public async Task AMultipleAnswerQuestion_IsCorrectOnlyWhenEveryCorrectOptionAndNothingElseWasChosen()
    {
        var multi = Fixtures.MultiQuestion();
        _attempts.ListSubmittedAnswersAsync(multi.Id, Arg.Any<CancellationToken>()).Returns(
        [
            new SubmittedAnswer(multi.CorrectSet(), 1),
            new SubmittedAnswer([multi.CorrectSet()[0]], 1),
            new SubmittedAnswer([..multi.CorrectSet(), multi.Options[2].Id], 1),
        ]);
        _bank.GetVersionsAsync(Arg.Any<IReadOnlyCollection<QuestionVersionRef>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([multi]));
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([multi]));

        var stats = await Source.ReadAsync(multi.Id, CancellationToken.None);

        Assert.Equal((3, 1), (stats.Answered, stats.Correct));
    }

    [Fact]
    public async Task AnAnswerIsJudgedAgainstTheVersionTheAttemptSat_NotTheKeyAsItIsNow()
    {
        // The key was corrected after the first candidate sat: version 1 said the first option, version 2 says another.
        var first = _question.Options[0].Id;
        var second = _question.Options[1].Id;
        var v1 = _question with { VersionNumber = 1 };
        var v2 = _question with
        {
            VersionNumber = 2,
            Options = _question.Options.Select(o => o with { IsCorrect = o.Id == second }).ToList(),
        };
        _bank.GetVersionsAsync(Arg.Any<IReadOnlyCollection<QuestionVersionRef>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([v1, v2]));
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([v2]));
        Answers(new SubmittedAnswer([first], 1), new SubmittedAnswer([second], 2), new SubmittedAnswer([first], 2));

        var stats = await Source.ReadAsync(_question.Id, CancellationToken.None);

        Assert.Equal((3, 2), (stats.Answered, stats.Correct));
    }

    [Fact]
    public async Task AnAttemptMadeBeforeVersionsWereKept_IsJudgedAgainstTheCurrentKey()
    {
        BankHasVersions(_question);
        Answers(new SubmittedAnswer([_question.Correct()], null));

        var stats = await Source.ReadAsync(_question.Id, CancellationToken.None);

        Assert.Equal((1, 1), (stats.Answered, stats.Correct));
    }
}
