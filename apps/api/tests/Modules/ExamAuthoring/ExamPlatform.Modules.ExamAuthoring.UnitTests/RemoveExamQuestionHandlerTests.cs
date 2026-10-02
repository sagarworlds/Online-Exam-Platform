using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

public class RemoveExamQuestionHandlerTests
{
    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly Exam exam = new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());
    private readonly RemoveExamQuestionHandler handler;

    public RemoveExamQuestionHandlerTests()
    {
        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        handler = new RemoveExamQuestionHandler(repository, unitOfWork);
    }

    [Fact]
    public async Task TakesTheQuestionOutOfTheSection_AndSaves()
    {
        var section = exam.AddSection("S", null);
        var questionId = Guid.NewGuid();
        exam.AddQuestion(section.Id, questionId, QuestionPlacement.Unfiled);

        await handler.HandleAsync(new RemoveExamQuestionCommand(exam.Id, section.Id, questionId), CancellationToken.None);

        Assert.Empty(section.Questions);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForAnUnknownExam_ThrowsAndSavesNothing()
    {
        var unknown = Guid.NewGuid();
        repository.GetByIdOrThrowAsync(unknown, Arg.Any<CancellationToken>()).Returns<Exam>(_ => throw new ExamNotFoundError(unknown));

        await Assert.ThrowsAsync<ExamNotFoundError>(() =>
            handler.HandleAsync(new RemoveExamQuestionCommand(unknown, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForAQuestionTheSectionDoesNotHold_ThrowsAndSavesNothing()
    {
        var section = exam.AddSection("S", null);

        await Assert.ThrowsAsync<QuestionNotInExamError>(() =>
            handler.HandleAsync(new RemoveExamQuestionCommand(exam.Id, section.Id, Guid.NewGuid()), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
