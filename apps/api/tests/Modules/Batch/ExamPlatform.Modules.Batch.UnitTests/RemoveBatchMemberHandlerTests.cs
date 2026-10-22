using ExamPlatform.Modules.Batch.Application;
using ExamPlatform.Modules.Batch.Application.Commands;
using ExamPlatform.Modules.Batch.Application.Ports;
using ExamPlatform.Modules.Batch.Domain.Events;
using ExamPlatform.Modules.Batch.Domain.Exceptions;
using NSubstitute;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.UnitTests;

public class RemoveBatchMemberHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private readonly IBatchRepository repository = Substitute.For<IBatchRepository>();
    private readonly IBatchUnitOfWork unitOfWork = Substitute.For<IBatchUnitOfWork>();
    private readonly BatchAggregate batch = new(Guid.NewGuid(), "Morning", null, 2, Guid.NewGuid(), Now.AddDays(-1));
    private readonly RemoveBatchMemberHandler handler;

    public RemoveBatchMemberHandlerTests()
    {
        repository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        handler = new RemoveBatchMemberHandler(repository, unitOfWork, new FakeClock(Now));
    }

    [Fact]
    public async Task TakesTheMemberOut_FreesTheSeat_AndSaves()
    {
        batch.AddMember("a@example.com", null, Now.AddDays(-1));
        batch.AddMember("b@example.com", null, Now.AddDays(-1));
        var leaving = batch.GetMemberByEmail("a@example.com")!;

        await handler.HandleAsync(new RemoveBatchMemberCommand(batch.Id, leaving.Id), CancellationToken.None);

        Assert.Null(batch.GetMember(leaving.Id));
        Assert.Equal(1, batch.GetActiveMemberCount());
        Assert.Equal(Now, batch.UpdatedAt);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsTheMemberRecord_MarkedRemoved_SoNothingIsErased()
    {
        batch.AddMember("a@example.com", null, Now.AddDays(-1));
        var leaving = batch.Members[0];

        await handler.HandleAsync(new RemoveBatchMemberCommand(batch.Id, leaving.Id), CancellationToken.None);

        var kept = Assert.Single(batch.Members);
        Assert.True(kept.IsDeleted);
        Assert.Equal("a@example.com", kept.Email);
    }

    [Fact]
    public async Task RaisesTheEventTheAuditTrailRecords()
    {
        batch.AddMember("a@example.com", null, Now.AddDays(-1));
        var leaving = batch.Members[0];
        batch.ClearDomainEvents();

        await handler.HandleAsync(new RemoveBatchMemberCommand(batch.Id, leaving.Id), CancellationToken.None);

        var removed = Assert.IsType<BatchMemberRemovedEvent>(Assert.Single(batch.DomainEvents));
        Assert.Equal(batch.Id, removed.BatchId);
        Assert.Equal(leaving.Id, removed.MemberId);
    }

    [Fact]
    public async Task TheSameAddressCanTakeTheSeatAgain()
    {
        batch.AddMember("a@example.com", null, Now.AddDays(-1));
        await handler.HandleAsync(new RemoveBatchMemberCommand(batch.Id, batch.Members[0].Id), CancellationToken.None);

        batch.AddMember("a@example.com", null, Now);

        Assert.Equal(1, batch.GetActiveMemberCount());
    }

    [Fact]
    public async Task ForAnUnknownBatch_ThrowsAndSavesNothing()
    {
        var unknown = Guid.NewGuid();

        await Assert.ThrowsAsync<BatchNotFoundError>(() =>
            handler.HandleAsync(new RemoveBatchMemberCommand(unknown, Guid.NewGuid()), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForAMemberTheBatchDoesNotHold_ThrowsAndSavesNothing()
    {
        batch.AddMember("a@example.com", null, Now.AddDays(-1));

        await Assert.ThrowsAsync<BatchMemberNotFoundError>(() =>
            handler.HandleAsync(new RemoveBatchMemberCommand(batch.Id, Guid.NewGuid()), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(1, batch.GetActiveMemberCount());
    }

    [Fact]
    public async Task ForAMemberWhoWasAlreadyRemoved_ThrowsNotFound_ratherThanRemovingTwice()
    {
        batch.AddMember("a@example.com", null, Now.AddDays(-1));
        var leaving = batch.Members[0];
        await handler.HandleAsync(new RemoveBatchMemberCommand(batch.Id, leaving.Id), CancellationToken.None);

        await Assert.ThrowsAsync<BatchMemberNotFoundError>(() =>
            handler.HandleAsync(new RemoveBatchMemberCommand(batch.Id, leaving.Id), CancellationToken.None));
    }
}
