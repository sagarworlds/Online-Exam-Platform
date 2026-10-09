using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

public class ExamDetailsHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly Clock clock = Substitute.For<Clock>();
    private readonly Exam exam = new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());

    public ExamDetailsHandlerTests()
    {
        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        clock.UtcNow.Returns(Now);
    }

    private UpdateExamDetailsHandler DetailsHandler() =>
        new(repository, unitOfWork, new ExamDtoFactory(Substitute.For<IBookCatalog>()), clock);

    [Fact]
    public async Task UpdatingTheDetails_SavesThem_AndReportsTheExamAsItNowIs()
    {
        var dto = await DetailsHandler().HandleAsync(new UpdateExamDetailsCommand(exam.Id, " Maths mock 2 ", "Chapters 1 to 4"), CancellationToken.None);

        Assert.Equal("Maths mock 2", dto.Name);
        Assert.Equal("Chapters 1 to 4", dto.Description);
        Assert.Equal(Now, exam.UpdatedAt);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdatingTheDetails_WithABlankName_ThrowsAndSavesNothing()
    {
        await Assert.ThrowsAsync<InvalidExamConfigError>(() =>
            DetailsHandler().HandleAsync(new UpdateExamDetailsCommand(exam.Id, null, "Description"), CancellationToken.None));

        Assert.Equal("Maths", exam.Name);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdatingTheDetails_OfAnUnknownExam_ThrowsAndSavesNothing()
    {
        var unknown = Guid.NewGuid();
        repository.GetByIdOrThrowAsync(unknown, Arg.Any<CancellationToken>()).Returns<Exam>(_ => throw new ExamNotFoundError(unknown));

        await Assert.ThrowsAsync<ExamNotFoundError>(() =>
            DetailsHandler().HandleAsync(new UpdateExamDetailsCommand(unknown, "Name", null), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditingASection_SavesTheNewNameAndTimeLimit()
    {
        var section = exam.AddSection("Old", null);

        await new EditSectionHandler(repository, unitOfWork)
            .HandleAsync(new EditSectionCommand(exam.Id, section.Id, "New", 900), CancellationToken.None);

        Assert.Equal("New", section.Name);
        Assert.Equal(900, section.TimeSeconds);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditingASection_WithBadInput_ThrowsAndSavesNothing()
    {
        var section = exam.AddSection("Old", null);

        await Assert.ThrowsAsync<InvalidExamConfigError>(() => new EditSectionHandler(repository, unitOfWork)
            .HandleAsync(new EditSectionCommand(exam.Id, section.Id, "", 900), CancellationToken.None));

        Assert.Equal("Old", section.Name);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
