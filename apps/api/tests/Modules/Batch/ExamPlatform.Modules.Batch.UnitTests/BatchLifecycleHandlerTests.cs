using ExamPlatform.Modules.Batch.Application;
using ExamPlatform.Modules.Batch.Application.Commands;
using ExamPlatform.Modules.Batch.Application.Ports;
using ExamPlatform.Modules.Batch.Domain.Exceptions;
using NSubstitute;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.UnitTests;

public class BatchLifecycleHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private readonly IBatchRepository repository = Substitute.For<IBatchRepository>();
    private readonly IBatchUnitOfWork unitOfWork = Substitute.For<IBatchUnitOfWork>();
    private readonly FakeClock clock = new(Now);

    [Fact]
    public async Task AddMember_UnknownBatch_ThrowsBatchNotFoundError()
    {
        var handler = new AddBatchMemberHandler(repository, unitOfWork, clock);

        await Assert.ThrowsAsync<BatchNotFoundError>(() =>
            handler.HandleAsync(new AddBatchMemberCommand(Guid.NewGuid(), "a@example.com", null), CancellationToken.None));
    }

    [Fact]
    public async Task Activate_UnknownBatch_ThrowsBatchNotFoundError()
    {
        var handler = new ActivateBatchHandler(repository, unitOfWork, clock);

        await Assert.ThrowsAsync<BatchNotFoundError>(() =>
            handler.HandleAsync(new ActivateBatchCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Close_UnknownBatch_ThrowsBatchNotFoundError()
    {
        var handler = new CloseBatchHandler(repository, unitOfWork, clock);

        await Assert.ThrowsAsync<BatchNotFoundError>(() =>
            handler.HandleAsync(new CloseBatchCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Activate_UsesTheInjectedClock()
    {
        var batch = new BatchAggregate(Guid.NewGuid(), "Batch A", null, 5, Guid.NewGuid(), Now.AddDays(-1));
        batch.AddMember("a@example.com", null, Now.AddDays(-1));
        repository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        await new ActivateBatchHandler(repository, unitOfWork, clock).HandleAsync(new ActivateBatchCommand(batch.Id), CancellationToken.None);

        Assert.Equal(Now, batch.UpdatedAt);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
