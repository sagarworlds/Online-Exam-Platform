using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>The author's marking scheme (FR-12): its rules, the exam's refusal once published, and the handler.</summary>
public class MarkingSchemeTests
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

    // ---- the scheme's own rules -------------------------------------------------------------------

    [Theory]
    [InlineData(4, -1, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(2.5, -0.5, -0.25)]
    [InlineData(100, -100, -100)]
    public void AReasonableScheme_IsValid(double correct, double incorrect, double unattempted)
    {
        new MarkingScheme((decimal)correct, (decimal)incorrect, (decimal)unattempted).EnsureValid();
    }

    [Theory]
    [InlineData(0, 0, 0)]          // a correct answer must earn something
    [InlineData(-1, 0, 0)]
    [InlineData(101, 0, 0)]
    [InlineData(1, 1, 0)]          // a wrong answer must not earn
    [InlineData(1, -101, 0)]
    [InlineData(1, 0, 1)]          // neither must a skipped question
    [InlineData(1, 0, -101)]
    [InlineData(1.001, 0, 0)]      // more than two decimal places
    [InlineData(1, -0.125, 0)]
    public void ASchemeOutOfRange_IsRefusedWithAReason(double correct, double incorrect, double unattempted)
    {
        var error = Assert.Throws<InvalidExamConfigError>(
            () => new MarkingScheme((decimal)correct, (decimal)incorrect, (decimal)unattempted).EnsureValid());

        Assert.Equal("invalid_exam_config", error.ErrorCode);
        Assert.Equal(400, error.HttpStatusCode);
    }

    // ---- the exam ---------------------------------------------------------------------------------

    [Fact]
    public void ANewExam_MarksOneForCorrectAndNothingElse()
    {
        Assert.Equal(new MarkingScheme(1m, 0m, 0m), DraftExam().Config.MarkingScheme);
    }

    [Fact]
    public void SettingIt_StoresTheMarks_AndKeepsTheRestOfTheConfig()
    {
        var exam = DraftExam();
        exam.SetResultRelease(ResultReleaseMode.Manual, null, Now);

        exam.SetMarkingScheme(new MarkingScheme(4m, -1m, 0m), Now.AddMinutes(5));

        Assert.Equal(new MarkingScheme(4m, -1m, 0m), exam.Config.MarkingScheme);
        Assert.Equal(ResultReleaseMode.Manual, exam.Config.ResultReleaseMode);
        Assert.Equal(Now.AddMinutes(5), exam.UpdatedAt);
    }

    [Fact]
    public void ARefusedScheme_ChangesNothing()
    {
        var exam = DraftExam();

        Assert.Throws<InvalidExamConfigError>(() => exam.SetMarkingScheme(new MarkingScheme(0m, 0m, 0m), Now));

        Assert.Equal(new MarkingScheme(), exam.Config.MarkingScheme);
    }

    [Fact]
    public void APublishedExam_RefusesIt_BecauseAttemptsScoredEarlierWouldDisagreeWithLaterOnes()
    {
        var exam = PublishedExam();

        Assert.Throws<ExamNotDraftError>(() => exam.SetMarkingScheme(new MarkingScheme(4m, -1m, 0m), Now));

        Assert.Equal(new MarkingScheme(), exam.Config.MarkingScheme);
    }

    [Fact]
    public void AnArchivedExam_RefusesIt()
    {
        var exam = PublishedExam();
        exam.Status = ExamStatus.Archived;

        Assert.Throws<ExamArchivedError>(() => exam.SetMarkingScheme(new MarkingScheme(4m, -1m, 0m), Now));
    }

    // ---- the handler ------------------------------------------------------------------------------

    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly Clock clock = Substitute.For<Clock>();

    private SetMarkingSchemeHandler Handler() => new(repository, unitOfWork, new ExamDtoFactory(Substitute.For<IBookCatalog>()), clock);

    private Exam Stored(Exam exam)
    {
        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        clock.UtcNow.Returns(Now);
        return exam;
    }

    [Fact]
    public async Task TheHandler_SavesTheMarks_AndReportsThemInTheExam()
    {
        var exam = Stored(DraftExam());

        var dto = await Handler().HandleAsync(new SetMarkingSchemeCommand(exam.Id, 4m, -1m, 0m), CancellationToken.None);

        Assert.Equal(new MarkingScheme(4m, -1m, 0m), dto.Config.MarkingScheme);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, -1.0, 0.0)]
    [InlineData(4.0, null, 0.0)]
    [InlineData(4.0, -1.0, null)]
    public async Task AMissingMark_IsRefusedWithAReason_AndNothingIsLoadedOrSaved(double? correct, double? incorrect, double? unattempted)
    {
        var exam = Stored(DraftExam());
        var command = new SetMarkingSchemeCommand(exam.Id, (decimal?)correct, (decimal?)incorrect, (decimal?)unattempted);

        await Assert.ThrowsAsync<InvalidExamConfigError>(() => Handler().HandleAsync(command, CancellationToken.None));

        await repository.DidNotReceive().GetByIdOrThrowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARefusedScheme_IsNotSaved()
    {
        var exam = Stored(DraftExam());

        await Assert.ThrowsAsync<InvalidExamConfigError>(
            () => Handler().HandleAsync(new SetMarkingSchemeCommand(exam.Id, 0m, 0m, 0m), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
