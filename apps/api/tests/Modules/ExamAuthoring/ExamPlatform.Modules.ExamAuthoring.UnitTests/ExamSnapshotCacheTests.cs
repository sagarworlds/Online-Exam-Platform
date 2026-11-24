using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>
/// How long a host keeps an exam's snapshot, and when it must not: a candidate's every save and page read asks for the exam, so it is kept
/// for a few seconds, and a change drops it at once (<see cref="CachedExamCatalogTests"/> and <see cref="SnapshotInvalidatingUnitOfWorkTests"/>
/// cover the two halves that use it).
/// </summary>
public class ExamSnapshotCacheTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);

    private readonly Clock _clock = Substitute.For<Clock>();

    public ExamSnapshotCacheTests() => _clock.UtcNow.Returns(Now);

    private ExamSnapshotCache Cache(int seconds = 15, int maxEntries = 500) => new(_clock, TimeSpan.FromSeconds(seconds), maxEntries);

    private static ExamSnapshot Snapshot(string name = "Maths") =>
        new(Guid.NewGuid(), name, null, true, Now, Now.AddHours(3), null, 3600, 1, 0, 0, []);

    [Fact]
    public void ASnapshotThatWasSet_IsServed_UntilItsLifetimeEnds()
    {
        var cache = Cache(seconds: 15);
        var snapshot = Snapshot();
        var id = Guid.NewGuid();
        cache.Set(id, snapshot, cache.Generation);

        Assert.True(cache.TryGet(id, out var served));
        Assert.Same(snapshot, served);

        _clock.UtcNow.Returns(Now.AddSeconds(14));
        Assert.True(cache.TryGet(id, out _));

        _clock.UtcNow.Returns(Now.AddSeconds(15));
        Assert.False(cache.TryGet(id, out _));
    }

    [Fact]
    public void Clearing_DropsEverything()
    {
        var cache = Cache();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        cache.Set(a, Snapshot(), cache.Generation);
        cache.Set(b, Snapshot(), cache.Generation);

        cache.Clear();

        Assert.False(cache.TryGet(a, out _));
        Assert.False(cache.TryGet(b, out _));
    }

    [Fact]
    public void AReadThatBeganBeforeAChange_CannotBringTheOldSnapshotBack()
    {
        var cache = Cache();
        var id = Guid.NewGuid();
        var generationWhenTheReadBegan = cache.Generation;

        cache.Clear(); // an exam is changed while the read is on its way
        cache.Set(id, Snapshot("the old exam"), generationWhenTheReadBegan);

        Assert.False(cache.TryGet(id, out _));
    }

    [Fact]
    public void AnEntryStoredWhileAChangeLands_IsNeverServed_EvenIfItIsInTheTableAfterTheClear()
    {
        // The generation is checked when an entry is read as well as when it is stored, so a store that races with a clear is harmless.
        var cache = Cache();
        var id = Guid.NewGuid();
        var generation = cache.Generation;
        cache.Set(id, Snapshot(), generation);

        cache.Clear();
        cache.Set(id, Snapshot("stale"), generation);

        Assert.False(cache.TryGet(id, out _));
    }

    [Fact]
    public void ALifetimeOfZero_TurnsTheCacheOff()
    {
        var cache = Cache(seconds: 0);
        var id = Guid.NewGuid();

        cache.Set(id, Snapshot(), cache.Generation);

        Assert.False(cache.IsEnabled);
        Assert.False(cache.TryGet(id, out _));
    }

    [Fact]
    public void ReachingTheLimit_StartsAgainRatherThanGrowing()
    {
        var cache = Cache(maxEntries: 2);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        cache.Set(first, Snapshot(), cache.Generation);
        cache.Set(second, Snapshot(), cache.Generation);
        cache.Set(third, Snapshot(), cache.Generation);

        Assert.True(cache.TryGet(third, out _));
        Assert.False(cache.TryGet(first, out _));
    }
}
