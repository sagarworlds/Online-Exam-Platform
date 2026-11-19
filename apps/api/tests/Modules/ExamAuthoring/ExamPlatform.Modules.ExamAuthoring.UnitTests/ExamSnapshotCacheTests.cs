using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>
/// How long a host keeps an exam's snapshot, and when it must not: a candidate's every save and page read asks for the exam, so it is kept
/// for a few seconds, and any change made through the authoring unit of work drops it at once.
/// </summary>
public class ExamSnapshotCacheTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);

    private readonly Clock _clock = Substitute.For<Clock>();

    public ExamSnapshotCacheTests() => _clock.UtcNow.Returns(Now);

    private ExamSnapshotCache Cache(int seconds = 15, int maxEntries = 500) => new(_clock, TimeSpan.FromSeconds(seconds), maxEntries);

    private static ExamSnapshot Snapshot(string name = "Maths") =>
        new(Guid.NewGuid(), name, null, true, Now, Now.AddHours(3), null, 3600, 1, 0, 0, []);

    // ---- the cache ------------------------------------------------------------------------------

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

    // ---- the catalog ----------------------------------------------------------------------------

    private static Exam PublishedExam()
    {
        var exam = new Exam(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid(), Now);
        exam.AddQuestion(exam.AddSection("S", null, Now).Id, Guid.NewGuid(), QuestionPlacement.Unfiled, Now);
        exam.Schedule(Now.AddHours(1), Now.AddHours(4), null, null, null, Now);
        exam.Publish(Now);
        return exam;
    }

    [Fact]
    public async Task TheCatalog_ReadsAnExamOnce_AndServesTheNextReadsFromTheCache()
    {
        var exam = PublishedExam();
        var repository = Substitute.For<IExamRepository>();
        repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);
        var catalog = new ExamCatalog(repository, Cache());

        var first = await catalog.FindAsync(exam.Id, CancellationToken.None);
        var second = await catalog.FindAsync(exam.Id, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
        await repository.Received(1).ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterAChange_TheCatalogReadsTheExamAgain()
    {
        var exam = PublishedExam();
        var repository = Substitute.For<IExamRepository>();
        repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);
        var cache = Cache();
        var catalog = new ExamCatalog(repository, cache);
        var before = await catalog.FindAsync(exam.Id, CancellationToken.None);
        Assert.True(before!.ContentProtection);

        exam.SetContentProtection(false, Now.AddMinutes(1));
        cache.Clear(); // what saving the change through the unit of work does

        var after = await catalog.FindAsync(exam.Id, CancellationToken.None);

        Assert.False(after!.ContentProtection);
        await repository.Received(2).ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReadThatIsOverTakenByAChange_DoesNotLeaveTheOldExamInTheCache()
    {
        var exam = PublishedExam();
        var cache = Cache();
        var repository = Substitute.For<IExamRepository>();
        // The change lands while the read is on its way back from the database: the read holds what the exam was before it.
        repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cache.Clear();
            return Task.FromResult<IReadOnlyList<Exam>>([exam]);
        });
        var catalog = new ExamCatalog(repository, cache);

        await catalog.FindAsync(exam.Id, CancellationToken.None);
        await catalog.FindAsync(exam.Id, CancellationToken.None);

        // The first result was read before the change, so it was not kept and the second call read again.
        await repository.Received(2).ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExamThatDoesNotExist_IsNotRemembered()
    {
        var repository = Substitute.For<IExamRepository>();
        repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        var catalog = new ExamCatalog(repository, Cache());

        Assert.Null(await catalog.FindAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await catalog.FindAsync(Guid.NewGuid(), CancellationToken.None));

        await repository.Received(2).ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutACache_EveryReadGoesToTheRepository()
    {
        var exam = PublishedExam();
        var repository = Substitute.For<IExamRepository>();
        repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);
        var catalog = new ExamCatalog(repository);

        await catalog.FindAsync(exam.Id, CancellationToken.None);
        await catalog.FindAsync(exam.Id, CancellationToken.None);

        await repository.Received(2).ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }
}
