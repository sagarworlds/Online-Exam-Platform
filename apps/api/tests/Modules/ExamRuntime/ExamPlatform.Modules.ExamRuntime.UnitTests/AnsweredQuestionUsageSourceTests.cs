using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>What the exam runtime tells the question bank about the questions candidates have answered.</summary>
public class AnsweredQuestionUsageSourceTests
{
    private readonly IAttemptRepository attempts = Substitute.For<IAttemptRepository>();

    [Fact]
    public async Task EveryAnsweredQuestion_IsOneAnsweredUse_WithNothingAboutWhoAnswered()
    {
        var answered = Guid.NewGuid();
        var untouched = Guid.NewGuid();
        attempts.FindAnsweredQuestionIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([answered]);

        var uses = await new AnsweredQuestionUsageSource(attempts).FindAsync([answered, untouched], CancellationToken.None);

        var use = Assert.Single(uses);
        Assert.Equal(answered, use.QuestionId);
        Assert.Equal(QuestionUseKind.Answered, use.Kind);
        Assert.Equal(AnsweredQuestionUsageSource.Description, use.Description);
    }

    [Fact]
    public async Task AskingAboutNoQuestions_DoesNotTouchTheDatabase()
    {
        var uses = await new AnsweredQuestionUsageSource(attempts).FindAsync([], CancellationToken.None);

        Assert.Empty(uses);
        await attempts.DidNotReceiveWithAnyArgs().FindAnsweredQuestionIdsAsync(default!, default);
    }
}
