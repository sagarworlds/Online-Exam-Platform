using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>How many attempts every enrolled candidate has at an exam (FR-12): the exam's rule and its handler.</summary>
public class AttemptLimitTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);

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
    public void ANewExam_AllowsOneAttempt_AsEveryExamDidBeforeTheSettingExisted()
    {
        Assert.Equal(1, DraftExam().Config.MaxAttempts);
    }

    [Fact]
    public void SettingIt_StoresTheNumber_AndKeepsTheRestOfTheConfig()
    {
        var exam = DraftExam();
        exam.Schedule(Now.AddHours(1), Now.AddHours(4), null, null, 1800, Now);
        exam.SetResultRelease(ResultReleaseMode.Manual, null, Now);

        exam.SetMaxAttempts(3, Now.AddMinutes(5));

        Assert.Equal(3, exam.Config.MaxAttempts);
        Assert.Equal(1800, exam.Config.TotalTimeSeconds);
        Assert.Equal(ResultReleaseMode.Manual, exam.Config.ResultReleaseMode);
        Assert.Equal(1m, exam.Config.MarkingScheme.CorrectMarks);
        Assert.Equal(Now.AddMinutes(5), exam.UpdatedAt);
    }

    [Theory]
    [InlineData(ExamConfig.FewestAttempts)]
    [InlineData(ExamConfig.MostAttempts)]
    public void TheEndsOfTheRange_AreAccepted(int attempts)
    {
        var exam = DraftExam();

        exam.SetMaxAttempts(attempts, Now);

        Assert.Equal(attempts, exam.Config.MaxAttempts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ExamConfig.MostAttempts + 1)]
    [InlineData(int.MaxValue)]
    public void ANumberOutsideTheRange_IsRefusedWithAReason_AndChangesNothing(int attempts)
    {
        var exam = DraftExam();
        exam.SetMaxAttempts(2, Now);

        var error = Assert.Throws<InvalidExamConfigError>(() => exam.SetMaxAttempts(attempts, Now));

        Assert.Equal("invalid_exam_config", error.ErrorCode);
        Assert.Equal(400, error.HttpStatusCode);
        Assert.Contains("1 to 10", error.Message);
        Assert.Equal(2, exam.Config.MaxAttempts);
    }

    [Fact]
    public void ItCanStillBeChangedOncePublished_BecauseItChangesNothingThatIsAskedOrScored()
    {
        var exam = PublishedExam();

        exam.SetMaxAttempts(4, Now);

        Assert.Equal(4, exam.Config.MaxAttempts);
        Assert.Equal(ExamStatus.Published, exam.Status);
    }

    [Fact]
    public void AnArchivedExam_CannotChangeIt()
    {
        var exam = PublishedExam();
        exam.Status = ExamStatus.Archived;

        Assert.Throws<ExamArchivedError>(() => exam.SetMaxAttempts(2, Now));
        Assert.Equal(1, exam.Config.MaxAttempts);
    }

    // ---- what other modules read ------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task TheExamCatalog_ReportsTheLimit_ToOtherModules(int attempts)
    {
        var exam = PublishedExam();
        exam.SetMaxAttempts(attempts, Now);
        var catalogRepository = Substitute.For<IExamRepository>();
        catalogRepository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);

        var snapshot = await new ExamCatalog(catalogRepository).FindAsync(exam.Id, CancellationToken.None);

        // The runtime decides who may start another attempt from this one number; it must be the author's, not a default.
        Assert.Equal(attempts, snapshot!.MaxAttempts);
    }

    // ---- the handler ------------------------------------------------------------------------------

    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly Clock clock = Substitute.For<Clock>();

    private SetMaxAttemptsHandler Handler() => new(repository, unitOfWork, new ExamDtoFactory(Substitute.For<IBookCatalog>()), clock);

    private Exam Stored(Exam exam)
    {
        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        clock.UtcNow.Returns(Now);
        return exam;
    }

    [Fact]
    public async Task TheHandler_SavesTheNumber_AndReportsItInTheExam()
    {
        var exam = Stored(DraftExam());

        var dto = await Handler().HandleAsync(new SetMaxAttemptsCommand(exam.Id, 3), CancellationToken.None);

        Assert.Equal(3, dto.Config.MaxAttempts);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABodyWithNoNumber_IsRefusedWithAReason_AndNothingIsLoadedOrSaved()
    {
        var exam = Stored(DraftExam());

        await Assert.ThrowsAsync<InvalidExamConfigError>(() => Handler().HandleAsync(new SetMaxAttemptsCommand(exam.Id, null), CancellationToken.None));

        await repository.DidNotReceive().GetByIdOrThrowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARefusedNumber_IsNotSaved()
    {
        var exam = Stored(DraftExam());

        await Assert.ThrowsAsync<InvalidExamConfigError>(() => Handler().HandleAsync(new SetMaxAttemptsCommand(exam.Id, 11), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(1, exam.Config.MaxAttempts);
    }
}
