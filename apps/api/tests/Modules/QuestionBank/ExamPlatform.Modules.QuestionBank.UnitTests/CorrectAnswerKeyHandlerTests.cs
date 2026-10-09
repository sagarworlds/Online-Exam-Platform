using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

public class CorrectAnswerKeyHandlerTests
{
    private sealed class FakeClock(DateTime now) : Clock
    {
        public DateTime UtcNow { get; } = now;
    }

    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private readonly IQuestionRepository _repository = Substitute.For<IQuestionRepository>();
    private readonly IQuestionBankUnitOfWork _unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
    private readonly IAttemptRescorer _rescorer = Substitute.For<IAttemptRescorer>();
    private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
    private readonly CorrectAnswerKeyHandler _handler;

    public CorrectAnswerKeyHandlerTests()
    {
        _handler = new CorrectAnswerKeyHandler(_repository, _unitOfWork, _rescorer, _auditLogger, new FakeClock(Now));
    }

    private Question Stored()
    {
        var question = Question.Create("Capital of France?", [new("Paris", true), new("Rome", false)], Guid.NewGuid(), Now);
        _repository.GetByIdAsync(question.Id, Arg.Any<CancellationToken>()).Returns(question);
        return question;
    }

    [Fact]
    public async Task HandleAsync_WhenTheKeyChanges_SavesFirst_ThenRescores_ThenAudits()
    {
        var question = Stored();
        var rome = question.Options[1].Id;
        _rescorer.RescoreForQuestionAsync(question.Id, "The marked option was wrong", Arg.Any<CancellationToken>()).Returns(3);

        var result = await _handler.HandleAsync(
            new CorrectAnswerKeyCommand(question.Id, [rome], "The marked option was wrong", Guid.NewGuid(), "SuperAdmin"), CancellationToken.None);

        Assert.True(result.KeyChanged);
        Assert.Equal(3, result.AttemptsRescored);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _rescorer.Received(1).RescoreForQuestionAsync(question.Id, "The marked option was wrong", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTheKeyChanges_RecordsAnAuditEntry_WithTheReasonAndTheRescoredCount()
    {
        var question = Stored();
        var rome = question.Options[1].Id;
        var actor = Guid.NewGuid();
        _rescorer.RescoreForQuestionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(5);

        await _handler.HandleAsync(
            new CorrectAnswerKeyCommand(question.Id, [rome], "Dispute upheld", actor, "SuperAdmin"), CancellationToken.None);

        await _auditLogger.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e =>
                e.ActorUserId == actor
                && e.ActorRole == "SuperAdmin"
                && e.Action == "QuestionBank.AnswerKeyCorrected"
                && e.EntityType == "Question"
                && e.EntityId == question.Id.ToString()
                && e.Metadata["reason"] == "Dispute upheld"
                && e.Metadata["attemptsRescored"] == "5"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTheKeyAlreadyMatches_ChangesNothing_AndNeverCallsRescoringOrAudit()
    {
        var question = Stored();
        var paris = question.Options[0].Id;

        var result = await _handler.HandleAsync(
            new CorrectAnswerKeyCommand(question.Id, [paris], "No actual change", Guid.NewGuid(), "SuperAdmin"), CancellationToken.None);

        Assert.False(result.KeyChanged);
        Assert.Equal(0, result.AttemptsRescored);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _rescorer.DidNotReceiveWithAnyArgs().RescoreForQuestionAsync(default!, default!, default);
        await _auditLogger.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_ForAnUnknownQuestion_ThrowsNotFound()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Question?)null);

        await Assert.ThrowsAsync<QuestionNotFoundError>(() =>
            _handler.HandleAsync(new CorrectAnswerKeyCommand(Guid.NewGuid(), [Guid.NewGuid()], "Reason", Guid.NewGuid(), "SuperAdmin"), CancellationToken.None));
    }
}
