using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

public class RemoveSectionHandlerTests
{
    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly Exam exam = new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());
    private readonly RemoveSectionHandler handler;

    public RemoveSectionHandlerTests()
    {
        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        handler = new RemoveSectionHandler(repository, unitOfWork);
    }

    [Fact]
    public async Task TakesTheSectionOut_AndSaves()
    {
        var section = exam.AddSection("S", null);

        await handler.HandleAsync(new RemoveSectionCommand(exam.Id, section.Id), CancellationToken.None);

        Assert.Empty(exam.Sections);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForAnUnknownExam_ThrowsAndSavesNothing()
    {
        var unknown = Guid.NewGuid();
        repository.GetByIdOrThrowAsync(unknown, Arg.Any<CancellationToken>()).Returns<Exam>(_ => throw new ExamNotFoundError(unknown));

        await Assert.ThrowsAsync<ExamNotFoundError>(() => handler.HandleAsync(new RemoveSectionCommand(unknown, Guid.NewGuid()), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForAnUnknownSection_ThrowsAndSavesNothing()
    {
        await Assert.ThrowsAsync<SectionNotFoundError>(() => handler.HandleAsync(new RemoveSectionCommand(exam.Id, Guid.NewGuid()), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
