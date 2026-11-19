using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>
/// The unit of work that drops the cached exam snapshots when a save has finished. Every change to an exam goes through one, so these are the
/// tests that a change is seen at once on the host that made it.
/// </summary>
public class SnapshotInvalidatingUnitOfWorkTests
{
    private readonly IExamAuthoringUnitOfWork _inner = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly IExamSnapshotCache _cache = Substitute.For<IExamSnapshotCache>();

    private SnapshotInvalidatingUnitOfWork UnitOfWork() => new(_inner, _cache);

    [Fact]
    public async Task ASave_IsPassedOn_AndItsResultReturned_AndTheSnapshotsAreDropped()
    {
        _inner.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(3);
        using var cancel = new CancellationTokenSource();

        var written = await UnitOfWork().SaveChangesAsync(cancel.Token);

        Assert.Equal(3, written);
        await _inner.Received(1).SaveChangesAsync(cancel.Token);
        _cache.Received(1).Clear();
    }

    [Fact]
    public async Task TheSnapshotsAreDropped_AfterTheSave_NotBefore()
    {
        // A read that lands between a clear and the save would store the old exam under the new generation, and serve it until it expired.
        var order = new List<string>();
        _inner.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            order.Add("save");
            return Task.FromResult(1);
        });
        _cache.When(c => c.Clear()).Do(_ => order.Add("clear"));

        await UnitOfWork().SaveChangesAsync(CancellationToken.None);

        Assert.Equal(["save", "clear"], order);
    }

    [Fact]
    public async Task AFailedSave_StillDropsTheSnapshots_AndTheFailureReachesTheCaller()
    {
        // A failed save may still have written part of a change; a cache that is cleared too often only costs a read.
        _inner.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<int>(new InvalidOperationException("the database said no")));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => UnitOfWork().SaveChangesAsync(CancellationToken.None));

        Assert.Equal("the database said no", thrown.Message);
        _cache.Received(1).Clear();
    }
}
