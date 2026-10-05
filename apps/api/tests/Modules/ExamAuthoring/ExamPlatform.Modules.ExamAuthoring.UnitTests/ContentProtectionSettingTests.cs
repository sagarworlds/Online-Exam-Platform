using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>The author's choice to turn off copying, pasting, right-click and printing during the exam (FR-23): the rule and the handler.</summary>
public class ContentProtectionSettingTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

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
    public void ANewExam_IsProtected_SoProtectionIsWhatCandidatesGetUnlessAnAuthorLiftsIt()
    {
        Assert.True(DraftExam().Config.ContentProtection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheAuthor_CanTurnItOnOrOff(bool enabled)
    {
        var exam = DraftExam();

        exam.SetContentProtection(enabled, Now.AddMinutes(5));

        Assert.Equal(enabled, exam.Config.ContentProtection);
        Assert.Equal(Now.AddMinutes(5), exam.UpdatedAt);
    }

    [Fact]
    public void ItChangesNothingElseInTheConfig()
    {
        var exam = DraftExam();
        exam.SetMaxAttempts(3, Now);
        var before = exam.Config;

        exam.SetContentProtection(false, Now);

        Assert.Equal(before with { ContentProtection = false }, exam.Config);
        Assert.Equal(before.MarkingScheme, exam.Config.MarkingScheme);
    }

    [Fact]
    public void ItCanChangeAfterPublishing_BecauseItChangesNothingAskedOrScored()
    {
        var exam = PublishedExam();

        exam.SetContentProtection(false, Now);

        Assert.False(exam.Config.ContentProtection);
    }

    [Fact]
    public void AnArchivedExam_RefusesIt()
    {
        var exam = PublishedExam();
        exam.Status = ExamStatus.Archived;

        Assert.Throws<ExamArchivedError>(() => exam.SetContentProtection(false, Now));
    }

    // ---- the handler ------------------------------------------------------------------------------

    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly Clock clock = Substitute.For<Clock>();

    private SetContentProtectionHandler Handler() => new(repository, unitOfWork, new ExamDtoFactory(Substitute.For<IBookCatalog>()), clock);

    [Fact]
    public async Task TheHandler_SavesTheChoice_AndReportsItInTheExam()
    {
        var exam = DraftExam();
        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        clock.UtcNow.Returns(Now);

        var dto = await Handler().HandleAsync(new SetContentProtectionCommand(exam.Id, false), CancellationToken.None);

        Assert.False(dto.Config.ContentProtection);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMissingChoice_IsRefusedWithAReason_AndNothingIsLoadedOrSaved()
    {
        var error = await Assert.ThrowsAsync<InvalidExamConfigError>(
            () => Handler().HandleAsync(new SetContentProtectionCommand(Guid.NewGuid(), null), CancellationToken.None));

        Assert.Equal("invalid_exam_config", error.ErrorCode);
        await repository.DidNotReceive().GetByIdOrThrowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
