using ExamPlatform.Modules.Batch.Application;
using ExamPlatform.Modules.Batch.Application.Commands;
using ExamPlatform.Modules.Batch.Application.Ports;
using ExamPlatform.Modules.Batch.Domain;
using ExamPlatform.Modules.Batch.Domain.Exceptions;
using NSubstitute;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.UnitTests;

public class CreateBatchHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithValidCommand_CreatesBatchAndReturnsDto()
    {
        var repository = Substitute.For<IBatchRepository>();
        var unitOfWork = Substitute.For<IBatchUnitOfWork>();
        var handler = new CreateBatchHandler(repository, unitOfWork, new FakeClock(new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc)));

        var examId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var command = new CreateBatchCommand(
            examId,
            "Batch A",
            "First batch of candidates",
            100,
            createdBy);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Batch A", result.Name);
        Assert.Equal("First batch of candidates", result.Description);
        Assert.Equal(100, result.MaxMembers);
        Assert.Equal(examId, result.ExamId);

        repository.Received(1).Add(Arg.Is<BatchAggregate>(b =>
            b.Name == "Batch A" &&
            b.ExamId == examId &&
            b.MaxMembers == 100));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithZeroMaxMembers_ThrowsInvalidBatchConfigError()
    {
        var handler = new CreateBatchHandler(
            Substitute.For<IBatchRepository>(),
            Substitute.For<IBatchUnitOfWork>(),
            new FakeClock(DateTime.UtcNow));

        var command = new CreateBatchCommand(
            Guid.NewGuid(),
            "Invalid Batch",
            null,
            0,
            Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidBatchConfigError>(() => handler.HandleAsync(command, CancellationToken.None));
    }
}
