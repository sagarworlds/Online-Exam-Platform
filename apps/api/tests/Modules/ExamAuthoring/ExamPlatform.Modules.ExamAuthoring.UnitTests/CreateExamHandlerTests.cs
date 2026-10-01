using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

public class CreateExamHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithValidCommand_CreatesExamAndReturnsDto()
    {
        var repository = Substitute.For<IExamRepository>();
        var unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
        var handler = new CreateExamHandler(repository, unitOfWork);

        var seriesId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var command = new CreateExamCommand(
            seriesId,
            "Mathematics Final",
            "Comprehensive mathematics assessment",
            createdBy);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Mathematics Final", result.Name);
        Assert.Equal("Comprehensive mathematics assessment", result.Description);
        Assert.Equal(seriesId, result.SeriesId);
        Assert.Equal(createdBy, result.CreatedBy);

        repository.Received(1).Add(Arg.Is<Exam>(e =>
            e.Name == "Mathematics Final" &&
            e.SeriesId == seriesId));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithEmptyName_ThrowsException(string name)
    {
        var handler = new CreateExamHandler(
            Substitute.For<IExamRepository>(),
            Substitute.For<IExamAuthoringUnitOfWork>());

        var command = new CreateExamCommand(Guid.NewGuid(), name, null, Guid.NewGuid());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(command, CancellationToken.None));
    }
}
