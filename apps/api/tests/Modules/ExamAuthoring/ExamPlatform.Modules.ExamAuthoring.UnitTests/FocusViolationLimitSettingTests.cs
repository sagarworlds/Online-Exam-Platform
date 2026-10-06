using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>The author's choice of how many times a candidate may leave the exam page before the attempt ends (FR-22): the rule and the handler.</summary>
public class FocusViolationLimitSettingTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);

    private static Exam DraftExam() => new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());

    private static Exam PublishedExam()
    {
        var exam = DraftExam();
        exam.AddQuestion(exam.AddSection("S", null).Id, Guid.NewGuid(), QuestionPlacement.Unfiled);
        exam.Schedule(Now.AddHours(1), Now.AddHours(4), null, null, null, Now);
        exam.Publish(Now);
        return exam;
    }

    [Fact]
    public void ANewExam_DoesNotWatchForDepartures_BecauseEndingASittingIsAChoiceAnAuthorMakesOnPurpose()
    {
        Assert.Equal(ExamConfig.NoViolationLimit, DraftExam().Config.FocusViolationLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(ExamConfig.MostViolations)]
    public void TheAuthor_CanSetAnyLimitInRange(int limit)
    {
        var exam = DraftExam();

        exam.SetFocusViolationLimit(limit, Now.AddMinutes(5));

        Assert.Equal(limit, exam.Config.FocusViolationLimit);
        Assert.Equal(Now.AddMinutes(5), exam.UpdatedAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ExamConfig.MostViolations + 1)]
    public void ALimitOutsideTheRange_IsRefused_AndTheOldOneStays(int limit)
    {
        var exam = DraftExam();
        exam.SetFocusViolationLimit(2, Now);

        var error = Assert.Throws<InvalidExamConfigError>(() => exam.SetFocusViolationLimit(limit, Now));

        Assert.Equal("invalid_exam_config", error.ErrorCode);
        Assert.Equal(2, exam.Config.FocusViolationLimit);
    }

    [Fact]
    public void ItChangesNothingElseInTheConfig()
    {
        var exam = DraftExam();
        exam.SetMaxAttempts(3, Now);
        var before = exam.Config;

        exam.SetFocusViolationLimit(4, Now);

        Assert.Equal(before with { FocusViolationLimit = 4 }, exam.Config);
        Assert.Equal(before.MarkingScheme, exam.Config.MarkingScheme);
    }

    [Fact]
    public void ItCanChangeAfterPublishing_BecauseItChangesNothingAskedOrScored()
    {
        var exam = PublishedExam();

        exam.SetFocusViolationLimit(3, Now);

        Assert.Equal(3, exam.Config.FocusViolationLimit);
    }

    [Fact]
    public void AnArchivedExam_RefusesIt()
    {
        var exam = PublishedExam();
        exam.Status = ExamStatus.Archived;

        Assert.Throws<ExamArchivedError>(() => exam.SetFocusViolationLimit(3, Now));
    }

    // ---- the handler ------------------------------------------------------------------------------

    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly Clock clock = Substitute.For<Clock>();

    private SetFocusViolationLimitHandler Handler() => new(repository, unitOfWork, new ExamDtoFactory(Substitute.For<IBookCatalog>()), clock);

    [Fact]
    public async Task TheHandler_SavesTheLimit_AndReportsItInTheExam()
    {
        var exam = DraftExam();
        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        clock.UtcNow.Returns(Now);

        var dto = await Handler().HandleAsync(new SetFocusViolationLimitCommand(exam.Id, 3), CancellationToken.None);

        Assert.Equal(3, dto.Config.FocusViolationLimit);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMissingLimit_IsRefusedWithAReason_AndNothingIsLoadedOrSaved()
    {
        var error = await Assert.ThrowsAsync<InvalidExamConfigError>(
            () => Handler().HandleAsync(new SetFocusViolationLimitCommand(Guid.NewGuid(), null), CancellationToken.None));

        Assert.Equal("invalid_exam_config", error.ErrorCode);
        await repository.DidNotReceive().GetByIdOrThrowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
