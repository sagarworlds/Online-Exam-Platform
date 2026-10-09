using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>The author's choice to shuffle questions and options (FR-12): the exam's rules and the handler.</summary>
public class ShuffleSettingTests
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

    [Fact]
    public void ANewExam_ShufflesNothing()
    {
        var config = DraftExam().Config;

        Assert.False(config.ShuffleQuestions);
        Assert.False(config.ShuffleOptions);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SettingIt_StoresBothChoices_AndKeepsTheRestOfTheConfig(bool questions, bool options)
    {
        var exam = DraftExam();
        exam.SetMarkingScheme(new MarkingScheme(4m, -1m, 0m), Now);

        exam.SetShuffle(questions, options, Now.AddMinutes(5));

        Assert.Equal(questions, exam.Config.ShuffleQuestions);
        Assert.Equal(options, exam.Config.ShuffleOptions);
        Assert.Equal(new MarkingScheme(4m, -1m, 0m), exam.Config.MarkingScheme);
        Assert.Equal(Now.AddMinutes(5), exam.UpdatedAt);
    }

    [Fact]
    public void APublishedExam_RefusesIt_BecauseTheOrderOfAttemptsAlreadyMadeWouldMove()
    {
        var exam = PublishedExam();

        Assert.Throws<ExamNotDraftError>(() => exam.SetShuffle(true, true, Now));

        Assert.False(exam.Config.ShuffleQuestions);
        Assert.False(exam.Config.ShuffleOptions);
    }

    [Fact]
    public void AnArchivedExam_RefusesIt()
    {
        var exam = PublishedExam();
        exam.Status = ExamStatus.Archived;

        Assert.Throws<ExamArchivedError>(() => exam.SetShuffle(true, true, Now));
    }

    // ---- the handler ------------------------------------------------------------------------------

    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly Clock clock = Substitute.For<Clock>();

    private SetShuffleHandler Handler() => new(repository, unitOfWork, new ExamDtoFactory(Substitute.For<IBookCatalog>()), clock);

    [Fact]
    public async Task TheHandler_SavesTheChoices_AndReportsThemInTheExam()
    {
        var exam = DraftExam();
        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        clock.UtcNow.Returns(Now);

        var dto = await Handler().HandleAsync(new SetShuffleCommand(exam.Id, true, false), CancellationToken.None);

        Assert.True(dto.Config.ShuffleQuestions);
        Assert.False(dto.Config.ShuffleOptions);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, null)]
    public async Task AMissingChoice_IsRefusedWithAReason_AndNothingIsLoadedOrSaved(bool? questions, bool? options)
    {
        var error = await Assert.ThrowsAsync<InvalidExamConfigError>(
            () => Handler().HandleAsync(new SetShuffleCommand(Guid.NewGuid(), questions, options), CancellationToken.None));

        Assert.Equal("invalid_exam_config", error.ErrorCode);
        await repository.DidNotReceive().GetByIdOrThrowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
