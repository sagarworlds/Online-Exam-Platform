using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

public class DeleteExamHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly Clock clock = Substitute.For<Clock>();
    private readonly Exam exam = new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());

    public DeleteExamHandlerTests()
    {
        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        clock.UtcNow.Returns(Now);
    }

    private static IExamDeletionGuard GuardSaying(params string[] reasons)
    {
        var guard = Substitute.For<IExamDeletionGuard>();
        guard.FindObjectionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(reasons);
        return guard;
    }

    private DeleteExamHandler HandlerWith(params IExamDeletionGuard[] guards) => new(repository, unitOfWork, guards, clock);

    [Fact]
    public async Task ADraftNothingObjectsTo_IsDeletedAndSaved()
    {
        await HandlerWith(GuardSaying(), GuardSaying()).HandleAsync(exam.Id, CancellationToken.None);

        Assert.True(exam.IsDeleted);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoGuardsRegistered_ADraftIsDeleted() // nothing else keeps the exam's id
    {
        await HandlerWith().HandleAsync(exam.Id, CancellationToken.None);

        Assert.True(exam.IsDeleted);
    }

    [Fact]
    public async Task WhenAGuardObjects_NothingIsDeleted_AndEveryReasonIsReported()
    {
        var handler = HandlerWith(GuardSaying("Candidates have been invited to it."), GuardSaying(), GuardSaying("A batch is assigned to it."));

        var error = await Assert.ThrowsAsync<ExamNotDeletableError>(() => handler.HandleAsync(exam.Id, CancellationToken.None));

        Assert.Equal(409, error.HttpStatusCode);
        Assert.Contains("Candidates have been invited to it.", error.Message);
        Assert.Contains("A batch is assigned to it.", error.Message);
        Assert.False(exam.IsDeleted);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APublishedExam_IsRefused_WithoutAskingTheGuards()
    {
        var section = exam.AddSection("S", null);
        exam.AddQuestion(section.Id, Guid.NewGuid(), QuestionPlacement.Unfiled);
        exam.Schedule(Now.AddDays(1), Now.AddDays(1).AddHours(3), null, null, 3600, Now);
        exam.Publish(Now);
        var guard = GuardSaying();

        await Assert.ThrowsAsync<ExamNotDeletableError>(() => HandlerWith(guard).HandleAsync(exam.Id, CancellationToken.None));

        await guard.DidNotReceive().FindObjectionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AGuardThatFails_FailsTheDeletion_SoAnExamIsNeverDeletedBecauseAModuleCouldNotBeAsked()
    {
        var failing = Substitute.For<IExamDeletionGuard>();
        failing.FindObjectionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<string>>(_ => throw new InvalidOperationException("database down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => HandlerWith(failing).HandleAsync(exam.Id, CancellationToken.None));

        Assert.False(exam.IsDeleted);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownExam_ThrowsNotFound_AndSavesNothing()
    {
        var unknown = Guid.NewGuid();
        repository.GetByIdOrThrowAsync(unknown, Arg.Any<CancellationToken>()).Returns<Exam>(_ => throw new ExamNotFoundError(unknown));

        await Assert.ThrowsAsync<ExamNotFoundError>(() => HandlerWith().HandleAsync(unknown, CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
