using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>
/// The catalog that reads an exam's snapshot through the cache: one read for a run of calls, a fresh read after any change, and no memory of
/// what the cache must not hold (an exam that does not exist, a read that a change overtook, anything but one exam by id).
/// </summary>
public class CachedExamCatalogTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);

    private readonly Clock _clock = Substitute.For<Clock>();
    private readonly IExamRepository _repository = Substitute.For<IExamRepository>();

    public CachedExamCatalogTests() => _clock.UtcNow.Returns(Now);

    private ExamSnapshotCache Cache(int seconds = 15) => new(_clock, TimeSpan.FromSeconds(seconds), 500);

    private CachedExamCatalog Catalog(IExamSnapshotCache cache) => new(new ExamCatalog(_repository), cache);

    private static Exam PublishedExam()
    {
        var exam = new Exam(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid(), Now);
        exam.AddQuestion(exam.AddSection("S", null, Now).Id, Guid.NewGuid(), QuestionPlacement.Unfiled, Now);
        exam.Schedule(Now.AddHours(1), Now.AddHours(4), null, null, null, Now);
        exam.Publish(Now);
        return exam;
    }

    private Task ReadsOfTheRepository(int times) =>
        _repository.Received(times).ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task AnExam_IsReadOnce_AndTheNextReadsAreServedFromTheCache()
    {
        var exam = PublishedExam();
        _repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);
        var catalog = Catalog(Cache());

        var first = await catalog.FindAsync(exam.Id, CancellationToken.None);
        var second = await catalog.FindAsync(exam.Id, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
        await ReadsOfTheRepository(1);
    }

    [Fact]
    public async Task AfterAChange_TheExamIsReadAgain()
    {
        var exam = PublishedExam();
        _repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);
        var cache = Cache();
        var catalog = Catalog(cache);
        var before = await catalog.FindAsync(exam.Id, CancellationToken.None);
        Assert.True(before!.ContentProtection);

        exam.SetContentProtection(false, Now.AddMinutes(1));
        cache.Clear(); // what saving the change through the unit of work does

        var after = await catalog.FindAsync(exam.Id, CancellationToken.None);

        Assert.False(after!.ContentProtection);
        await ReadsOfTheRepository(2);
    }

    [Fact]
    public async Task AReadThatIsOvertakenByAChange_DoesNotLeaveTheOldExamInTheCache()
    {
        var exam = PublishedExam();
        var cache = Cache();
        // The change lands while the read is on its way back from the database: the read holds what the exam was before it.
        _repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cache.Clear();
            return Task.FromResult<IReadOnlyList<Exam>>([exam]);
        });
        var catalog = Catalog(cache);

        await catalog.FindAsync(exam.Id, CancellationToken.None);
        await catalog.FindAsync(exam.Id, CancellationToken.None);

        // The first result was read before the change, so it was not kept and the second call read again.
        await ReadsOfTheRepository(2);
    }

    [Fact]
    public async Task AnExamThatDoesNotExist_IsNotRemembered()
    {
        _repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        var catalog = Catalog(Cache());

        Assert.Null(await catalog.FindAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await catalog.FindAsync(Guid.NewGuid(), CancellationToken.None));

        await ReadsOfTheRepository(2);
    }

    [Fact]
    public async Task ACacheThatIsOff_LeavesEveryReadToTheRepository()
    {
        var exam = PublishedExam();
        _repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);
        var catalog = Catalog(Cache(seconds: 0));

        await catalog.FindAsync(exam.Id, CancellationToken.None);
        await catalog.FindAsync(exam.Id, CancellationToken.None);

        await ReadsOfTheRepository(2);
    }

    [Fact]
    public async Task ReadsOfSeveralExams_AreNeverServedFromTheCache()
    {
        // Which exams are published, and which are about to start, must be current: only the one-exam lookup is kept.
        var exam = PublishedExam();
        _repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);
        _repository.ListPublishedStartingBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([exam]);
        var catalog = Catalog(Cache());
        await catalog.FindAsync(exam.Id, CancellationToken.None);

        await catalog.FindPublishedAsync([exam.Id], CancellationToken.None);
        await catalog.FindPublishedAsync([exam.Id], CancellationToken.None);
        await catalog.FindPublishedStartingBetweenAsync(Now, Now.AddDays(1), CancellationToken.None);
        await catalog.FindPublishedStartingBetweenAsync(Now, Now.AddDays(1), CancellationToken.None);

        await ReadsOfTheRepository(3); // the lookup, then the two published reads
        await _repository.Received(2).ListPublishedStartingBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
