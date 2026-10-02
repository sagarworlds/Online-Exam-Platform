using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

public class DeleteQuestionHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly IQuestionRepository repository = Substitute.For<IQuestionRepository>();
    private readonly IQuestionBankUnitOfWork unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
    private readonly IQuestionUsageSource usageSource = Substitute.For<IQuestionUsageSource>();
    private readonly DeleteQuestionHandler handler;

    public DeleteQuestionHandlerTests()
    {
        handler = new DeleteQuestionHandler(repository, unitOfWork, new QuestionUsageReader([usageSource]));
        usageSource.FindAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private Question Stored()
    {
        var question = Question.Create("Q?", [new("A", true), new("B", false)], Guid.NewGuid(), Now);
        repository.GetByIdAsync(question.Id, Arg.Any<CancellationToken>()).Returns(question);
        return question;
    }

    private void UsedBy(Question question, params QuestionUse[] uses) =>
        usageSource.FindAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(uses.Select(u => u with { QuestionId = question.Id }).ToList());

    private static QuestionUse InExam(string name) => new(Guid.Empty, QuestionUseKind.InExam, name);

    [Fact]
    public async Task AQuestionNothingUses_IsRemovedAndTheChangeSaved()
    {
        var question = Stored();

        await handler.HandleAsync(question.Id, CancellationToken.None);

        repository.Received(1).Remove(question);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownQuestion_IsNotFound_AndNothingIsRemoved()
    {
        await Assert.ThrowsAsync<QuestionNotFoundError>(() => handler.HandleAsync(Guid.NewGuid(), CancellationToken.None));

        repository.DidNotReceive().Remove(Arg.Any<Question>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AQuestionInAnExam_IsRefused_NamingTheExam_AndNothingIsRemoved()
    {
        var question = Stored();
        UsedBy(question, InExam("Maths mock"));

        var error = await Assert.ThrowsAsync<QuestionInUseError>(() => handler.HandleAsync(question.Id, CancellationToken.None));

        Assert.Equal("It is part of the exam \"Maths mock\", so it cannot be deleted.", error.Message);
        Assert.Equal("question_in_use", error.ErrorCode);
        Assert.Equal(409, error.HttpStatusCode);
        repository.DidNotReceive().Remove(Arg.Any<Question>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManyExams_AreNamedUpToThree_ThenCounted()
    {
        var question = Stored();
        UsedBy(question, InExam("A"), InExam("B"), InExam("C"), InExam("D"), InExam("E"));

        var error = await Assert.ThrowsAsync<QuestionInUseError>(() => handler.HandleAsync(question.Id, CancellationToken.None));

        Assert.Equal("It is part of the exams \"A\", \"B\", \"C\" and 2 more, so it cannot be deleted.", error.Message);
    }

    [Fact]
    public async Task AnAnsweredQuestion_IsRefusedEvenIfNoExamClaimsIt()
    {
        var question = Stored();
        UsedBy(question, new QuestionUse(Guid.Empty, QuestionUseKind.Answered, "Answered by candidates"));

        var error = await Assert.ThrowsAsync<QuestionInUseError>(() => handler.HandleAsync(question.Id, CancellationToken.None));

        Assert.Equal("Candidates have answered this question, so it cannot be deleted.", error.Message);
        repository.DidNotReceive().Remove(Arg.Any<Question>());
    }
}
