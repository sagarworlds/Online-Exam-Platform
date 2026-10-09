using ExamPlatform.Modules.Batch.Application.Ports;
using ExamPlatform.Modules.Batch.Application.Queries;
using ExamPlatform.Modules.Batch.Domain.Exceptions;
using NSubstitute;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.UnitTests;

public class BatchQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ListBatches_ReturnsEveryBatch()
    {
        var first = new BatchAggregate(Guid.NewGuid(), "Morning", null, 10, Guid.NewGuid(), Now);
        var second = new BatchAggregate(Guid.NewGuid(), "Evening", null, 10, Guid.NewGuid(), Now);
        IReadOnlyList<BatchAggregate> all = [first, second];
        var repository = Substitute.For<IBatchRepository>();
        repository.ListAsync(Arg.Any<CancellationToken>()).Returns(all);

        var batches = await new ListBatchesHandler(repository).HandleAsync(CancellationToken.None);

        Assert.Equal(new[] { first.Id, second.Id }, batches.Select(b => b.Id));
    }

    [Fact]
    public async Task ListBatchMembers_LeavesOutAMemberWhoWasRemoved()
    {
        var batch = new BatchAggregate(Guid.NewGuid(), "Morning", null, 10, Guid.NewGuid(), Now);
        batch.AddMember("kept@example.com", null, Now);
        batch.AddMember("removed@example.com", null, Now);
        batch.RemoveMember(batch.GetMemberByEmail("removed@example.com")!.Id, Now);
        var repository = Substitute.For<IBatchRepository>();
        repository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        var members = await new ListBatchMembersHandler(repository).HandleAsync(batch.Id, CancellationToken.None);

        var member = Assert.Single(members);
        Assert.Equal("kept@example.com", member.Email);
    }

    [Fact]
    public async Task ListBatchMembers_ForAnUnknownBatch_ThrowsNotFound()
    {
        var repository = Substitute.For<IBatchRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BatchAggregate?)null);

        await Assert.ThrowsAsync<BatchNotFoundError>(() =>
            new ListBatchMembersHandler(repository).HandleAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
