using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>What exams tell the question bank about the questions they hold.</summary>
public class ExamQuestionUsageSourceTests
{
    private readonly IExamRepository repository = Substitute.For<IExamRepository>();

    [Fact]
    public async Task EachExamThatHoldsAQuestion_IsOneUse_NamedAfterTheExam()
    {
        var q = Guid.NewGuid();
        repository.ListUsesOfQuestionsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(
        [
            new ExamQuestionUse(q, Guid.NewGuid(), "Maths mock", ExamStatus.Published),
            new ExamQuestionUse(q, Guid.NewGuid(), "Algebra draft", ExamStatus.Draft),
        ]);

        var uses = await new ExamQuestionUsageSource(repository).FindAsync([q], CancellationToken.None);

        Assert.Equal(2, uses.Count);
        Assert.All(uses, u => Assert.Equal(QuestionUseKind.InExam, u.Kind));
        Assert.Equal(["Algebra draft", "Maths mock"], uses.Select(u => u.Description).Order());
    }

    [Fact]
    public async Task ADraftExam_CountsToo_BecauseItReadsItsQuestionsLiveJustAsAPublishedOneDoes()
    {
        var q = Guid.NewGuid();
        repository.ListUsesOfQuestionsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new ExamQuestionUse(q, Guid.NewGuid(), "Draft", ExamStatus.Draft)]);

        var uses = await new ExamQuestionUsageSource(repository).FindAsync([q], CancellationToken.None);

        Assert.Single(uses);
    }

    [Fact]
    public async Task AskingAboutNoQuestions_DoesNotTouchTheDatabase()
    {
        var uses = await new ExamQuestionUsageSource(repository).FindAsync([], CancellationToken.None);

        Assert.Empty(uses);
        await repository.DidNotReceiveWithAnyArgs().ListUsesOfQuestionsAsync(default!, default);
    }
}
