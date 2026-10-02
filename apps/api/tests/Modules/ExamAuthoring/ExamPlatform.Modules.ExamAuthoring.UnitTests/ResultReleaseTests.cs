using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>When candidates may see which of their answers were right: the exam's rule, its handlers and what other modules read.</summary>
public class ResultReleaseTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReleaseAt = Now.AddDays(2);

    private static Exam DraftExam() => new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());

    private static Exam PublishedExam()
    {
        var exam = DraftExam();
        exam.AddQuestion(exam.AddSection("S", null).Id, Guid.NewGuid(), QuestionPlacement.Unfiled);
        exam.Schedule(Now.AddHours(1), Now.AddHours(4), null, null, null, Now);
        exam.Publish(Now);
        return exam;
    }

    // ---- the rule ---------------------------------------------------------------------------------

    [Fact]
    public void ANewExam_ShowsAnswersRightAfterSubmitting()
    {
        var exam = DraftExam();

        Assert.Equal(ResultReleaseMode.Instant, exam.Config.ResultReleaseMode);
        Assert.Null(exam.Config.ResultReleaseTime);
    }

    [Fact]
    public void Scheduled_KeepsTheTime_AndTheRestOfTheConfig()
    {
        var exam = DraftExam();
        exam.Schedule(Now.AddHours(1), Now.AddHours(4), null, null, 1800, Now);

        exam.SetResultRelease(ResultReleaseMode.Scheduled, ReleaseAt, Now);

        Assert.Equal(ResultReleaseMode.Scheduled, exam.Config.ResultReleaseMode);
        Assert.Equal(ReleaseAt, exam.Config.ResultReleaseTime);
        Assert.Equal(1800, exam.Config.TotalTimeSeconds);
        Assert.Equal(1m, exam.Config.MarkingScheme.CorrectMarks);
    }

    [Fact]
    public void Scheduled_WithoutATime_IsRefused()
    {
        var exam = DraftExam();

        Assert.Throws<InvalidExamConfigError>(() => exam.SetResultRelease(ResultReleaseMode.Scheduled, null, Now));
        Assert.Equal(ResultReleaseMode.Instant, exam.Config.ResultReleaseMode);
    }

    [Theory]
    [InlineData(ResultReleaseMode.Instant)]
    [InlineData(ResultReleaseMode.Manual)]
    public void InstantAndManual_DropAnyTime_SoAStaleTimeCannotReleaseAnExamByItself(ResultReleaseMode mode)
    {
        var exam = DraftExam();
        exam.SetResultRelease(ResultReleaseMode.Scheduled, ReleaseAt, Now);

        exam.SetResultRelease(mode, ReleaseAt, Now);

        Assert.Equal(mode, exam.Config.ResultReleaseMode);
        Assert.Null(exam.Config.ResultReleaseTime);
    }

    [Fact]
    public void AModeThatIsNotOneOfTheThree_IsRefused()
    {
        Assert.Throws<InvalidExamConfigError>(() => DraftExam().SetResultRelease((ResultReleaseMode)42, null, Now));
    }

    [Fact]
    public void ThePolicy_CanStillBeChangedOncePublished_BecauseTheReviewIsWorkedOutWhenItIsAskedFor()
    {
        var exam = PublishedExam();

        exam.SetResultRelease(ResultReleaseMode.Manual, null, Now);

        Assert.Equal(ResultReleaseMode.Manual, exam.Config.ResultReleaseMode);
    }

    [Fact]
    public void AnArchivedExam_CannotChangeIt()
    {
        var exam = PublishedExam();
        exam.Status = ExamStatus.Archived;

        Assert.Throws<ExamArchivedError>(() => exam.SetResultRelease(ResultReleaseMode.Manual, null, Now));
    }

    // ---- releasing by hand ------------------------------------------------------------------------

    [Fact]
    public void ReleasingByHand_SetsTheTimeToNow()
    {
        var exam = PublishedExam();
        exam.SetResultRelease(ResultReleaseMode.Manual, null, Now);

        exam.ReleaseResults(Now.AddHours(5));

        Assert.Equal(Now.AddHours(5), exam.Config.ResultReleaseTime);
        Assert.Equal(ResultReleaseMode.Manual, exam.Config.ResultReleaseMode);
    }

    [Fact]
    public void ReleasingTwice_KeepsTheFirstTime()
    {
        var exam = PublishedExam();
        exam.SetResultRelease(ResultReleaseMode.Manual, null, Now);
        exam.ReleaseResults(Now.AddHours(5));

        exam.ReleaseResults(Now.AddHours(9));

        Assert.Equal(Now.AddHours(5), exam.Config.ResultReleaseTime);
    }

    [Fact]
    public void SwitchingBackToManual_HoldsTheAnswersBackAgain()
    {
        var exam = PublishedExam();
        exam.SetResultRelease(ResultReleaseMode.Manual, null, Now);
        exam.ReleaseResults(Now.AddHours(5));

        exam.SetResultRelease(ResultReleaseMode.Manual, null, Now.AddHours(6));

        Assert.Null(exam.Config.ResultReleaseTime);
    }

    [Fact]
    public void OnlyAManualExam_IsReleasedByHand()
    {
        var instant = PublishedExam();
        var scheduled = PublishedExam();
        scheduled.SetResultRelease(ResultReleaseMode.Scheduled, ReleaseAt, Now);

        Assert.Throws<InvalidExamConfigError>(() => instant.ReleaseResults(Now));
        Assert.Throws<InvalidExamConfigError>(() => scheduled.ReleaseResults(Now));
        Assert.Equal(ReleaseAt, scheduled.Config.ResultReleaseTime);
    }

    [Fact]
    public void ADraft_HasNoAnswersToRelease()
    {
        var exam = DraftExam();
        exam.SetResultRelease(ResultReleaseMode.Manual, null, Now);

        Assert.Throws<InvalidExamConfigError>(() => exam.ReleaseResults(Now));
        Assert.Null(exam.Config.ResultReleaseTime);
    }

    // ---- handlers ---------------------------------------------------------------------------------

    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly Clock clock = Substitute.For<Clock>();

    private ExamDtoFactory Dtos() => new(Substitute.For<IBookCatalog>());

    private Exam Stored(Exam exam)
    {
        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        clock.UtcNow.Returns(Now);
        return exam;
    }

    [Fact]
    public async Task TheHandler_SavesTheChoice_AndReportsItInTheExam()
    {
        var exam = Stored(DraftExam());
        var handler = new SetResultReleaseHandler(repository, unitOfWork, Dtos(), clock);

        var dto = await handler.HandleAsync(new SetResultReleaseCommand(exam.Id, ResultReleaseMode.Scheduled, ReleaseAt), CancellationToken.None);

        Assert.Equal(ResultReleaseMode.Scheduled, dto.Config.ResultReleaseMode);
        Assert.Equal(ReleaseAt, dto.Config.ResultReleaseTime);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATimeSentWithoutAnOffset_IsReadAsUtc_NotAsServerLocalTime()
    {
        var exam = Stored(DraftExam());
        var handler = new SetResultReleaseHandler(repository, unitOfWork, Dtos(), clock);
        var unspecified = DateTime.SpecifyKind(ReleaseAt, DateTimeKind.Unspecified);

        await handler.HandleAsync(new SetResultReleaseCommand(exam.Id, ResultReleaseMode.Scheduled, unspecified), CancellationToken.None);

        Assert.Equal(DateTimeKind.Utc, exam.Config.ResultReleaseTime!.Value.Kind);
        Assert.Equal(ReleaseAt, exam.Config.ResultReleaseTime);
    }

    [Fact]
    public async Task ABodyWithoutAMode_IsRefusedWithAReason_AndSavesNothing()
    {
        var exam = Stored(DraftExam());
        var handler = new SetResultReleaseHandler(repository, unitOfWork, Dtos(), clock);

        await Assert.ThrowsAsync<InvalidExamConfigError>(() => handler.HandleAsync(new SetResultReleaseCommand(exam.Id, null, null), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnInvalidChoice_SavesNothing()
    {
        var exam = Stored(DraftExam());
        var handler = new SetResultReleaseHandler(repository, unitOfWork, Dtos(), clock);

        await Assert.ThrowsAsync<InvalidExamConfigError>(() => handler.HandleAsync(new SetResultReleaseCommand(exam.Id, ResultReleaseMode.Scheduled, null), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheReleaseHandler_UsesTheServerClock_AndSaves()
    {
        var exam = Stored(PublishedExam());
        exam.SetResultRelease(ResultReleaseMode.Manual, null, Now);
        var handler = new ReleaseResultsHandler(repository, unitOfWork, Dtos(), clock);

        var dto = await handler.HandleAsync(exam.Id, CancellationToken.None);

        Assert.Equal(Now, dto.Config.ResultReleaseTime);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- what other modules read ------------------------------------------------------------------

    [Theory]
    [InlineData(ResultReleaseMode.Instant, ExamResultReleaseMode.Instant)]
    [InlineData(ResultReleaseMode.Scheduled, ExamResultReleaseMode.Scheduled)]
    [InlineData(ResultReleaseMode.Manual, ExamResultReleaseMode.Manual)]
    public async Task TheExamCatalog_ReportsEachMode_ToOtherModules(ResultReleaseMode mode, ExamResultReleaseMode expected)
    {
        var exam = PublishedExam();
        exam.SetResultRelease(mode, mode == ResultReleaseMode.Scheduled ? ReleaseAt : null, Now);
        repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);

        var snapshot = await new ExamCatalog(repository).FindAsync(exam.Id, CancellationToken.None);

        Assert.Equal(expected, snapshot!.ResultRelease);
        Assert.Equal(mode == ResultReleaseMode.Scheduled ? ReleaseAt : null, snapshot.ResultReleaseTimeUtc);
    }

    [Fact]
    public void EveryModeThatTheExamModuleKnows_HasACounterpartInTheContract()
    {
        // Guards the hand-written mapping in ExamCatalog: a new mode added on one side must be noticed.
        Assert.Equal(
            Enum.GetNames<ResultReleaseMode>().Order(),
            Enum.GetNames<ExamResultReleaseMode>().Order());
    }
}
