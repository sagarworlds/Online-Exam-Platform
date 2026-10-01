using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
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

    [Fact]
    public async Task HandleAsync_WithoutSeries_CreatesAStandaloneExam()
    {
        var repository = Substitute.For<IExamRepository>();
        var handler = new CreateExamHandler(repository, Substitute.For<IExamAuthoringUnitOfWork>());

        var result = await handler.HandleAsync(
            new CreateExamCommand(SeriesId: null, "Standalone", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Null(result.SeriesId);
        repository.Received(1).Add(Arg.Is<Exam>(e => e.SeriesId == null));
    }

    [Fact]
    public async Task HandleAsync_WithEmptySeriesGuid_ThrowsInvalidExamConfigErrorAndStoresNothing()
    {
        var repository = Substitute.For<IExamRepository>();
        var unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
        var handler = new CreateExamHandler(repository, unitOfWork);

        var command = new CreateExamCommand(Guid.Empty, "Exam", null, Guid.NewGuid());

        var error = await Assert.ThrowsAsync<InvalidExamConfigError>(
            () => handler.HandleAsync(command, CancellationToken.None));

        // A typed 400, not a 500: the empty GUID is what a form posts for a blank series field.
        Assert.Equal(400, error.HttpStatusCode);
        repository.DidNotReceive().Add(Arg.Any<Exam>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
