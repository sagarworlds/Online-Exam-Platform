using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

public class ExamInstructionsTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly IExamRepository examRepository = Substitute.For<IExamRepository>();
    private readonly IInstructionTemplateRepository templateRepository = Substitute.For<IInstructionTemplateRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly IAuditLogger auditLogger = Substitute.For<IAuditLogger>();
    private readonly IRequestContext requestContext = Substitute.For<IRequestContext>();
    private readonly Clock clock = Substitute.For<Clock>();
    private readonly Exam exam = new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());

    public ExamInstructionsTests()
    {
        examRepository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        clock.UtcNow.Returns(Now);
    }

    private InstructionAudit Audit() => new(auditLogger, requestContext);

    private SetExamInstructionsHandler SetHandler() =>
        new(examRepository, unitOfWork, new ExamDtoFactory(Substitute.For<IBookCatalog>()), Audit(), clock);

    private UseInstructionTemplateHandler UseHandler() =>
        new(examRepository, templateRepository, unitOfWork, new ExamDtoFactory(Substitute.For<IBookCatalog>()), Audit(), clock);

    [Fact]
    public void SetInstructions_OnADraft_TrimsAndStoresThem()
    {
        exam.SetInstructions("  Read every question twice.  ", Now);

        Assert.Equal("Read every question twice.", exam.Instructions);
        Assert.Equal(Now, exam.UpdatedAt);
    }

    [Fact]
    public void SetInstructions_WithBlankText_ClearsThem()
    {
        exam.SetInstructions("Something", Now);

        exam.SetInstructions("   ", Now);

        Assert.Null(exam.Instructions);
    }

    [Fact]
    public void SetInstructions_OverTheLimit_Throws()
    {
        Assert.Throws<InvalidExamConfigError>(() =>
            exam.SetInstructions(new string('x', Exam.MaxInstructionsLength + 1), Now));
        Assert.Null(exam.Instructions);
    }

    [Fact]
    public void SetInstructions_OnAPublishedExam_Throws()
    {
        // A candidate acknowledges the instructions before each attempt, so the text is fixed once candidates can sit the exam.
        exam.Status = ExamStatus.Published;

        Assert.Throws<ExamNotDraftError>(() => exam.SetInstructions("Changed", Now));
        Assert.Null(exam.Instructions);
    }

    [Fact]
    public void SetInstructions_OnAnArchivedExam_Throws()
    {
        exam.Status = ExamStatus.Archived;

        Assert.Throws<ExamArchivedError>(() => exam.SetInstructions("Changed", Now));
    }

    [Fact]
    public async Task SetInstructionsHandler_SavesAndAudits()
    {
        var dto = await SetHandler().HandleAsync(new SetExamInstructionsCommand(exam.Id, "Sit quietly."), CancellationToken.None);

        Assert.Equal("Sit quietly.", dto.Instructions);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await auditLogger.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e => e.Action == "ExamAuthoring.ExamInstructionsChanged" && e.EntityId == exam.Id.ToString()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UseTemplate_CopiesTheTemplateText_IntoTheExam()
    {
        var template = InstructionTemplate.Create("Board rules", "Bring a pencil.", Now);
        templateRepository.GetOrThrowAsync(template.Id, Arg.Any<CancellationToken>()).Returns(template);

        var dto = await UseHandler().HandleAsync(new UseInstructionTemplateCommand(exam.Id, template.Id), CancellationToken.None);

        Assert.Equal("Bring a pencil.", dto.Instructions);
        Assert.Equal("Bring a pencil.", exam.Instructions);
    }

    [Fact]
    public async Task UseTemplate_LaterEditsToTheTemplate_DoNotChangeTheExam()
    {
        // The exam keeps its own copy: a template is a starting point, not a live link, so a published or in-use exam never shifts.
        var template = InstructionTemplate.Create("Board rules", "Bring a pencil.", Now);
        templateRepository.GetOrThrowAsync(template.Id, Arg.Any<CancellationToken>()).Returns(template);
        await UseHandler().HandleAsync(new UseInstructionTemplateCommand(exam.Id, template.Id), CancellationToken.None);

        template.Change("Board rules", "Bring a calculator.", Now.AddDays(1));

        Assert.Equal("Bring a pencil.", exam.Instructions);
    }

    [Fact]
    public async Task UseTemplate_ForAMissingTemplate_ThrowsAndSavesNothing()
    {
        var missing = Guid.NewGuid();
        templateRepository.GetOrThrowAsync(missing, Arg.Any<CancellationToken>())
            .Returns<Task<InstructionTemplate>>(_ => throw new InstructionTemplateNotFoundError(missing));

        await Assert.ThrowsAsync<InstructionTemplateNotFoundError>(() =>
            UseHandler().HandleAsync(new UseInstructionTemplateCommand(exam.Id, missing), CancellationToken.None));

        Assert.Null(exam.Instructions);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
