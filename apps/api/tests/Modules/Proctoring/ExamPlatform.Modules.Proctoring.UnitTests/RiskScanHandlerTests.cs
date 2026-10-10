using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.Proctoring.Application;
using ExamPlatform.Modules.Proctoring.Application.Commands;
using ExamPlatform.Modules.Proctoring.Application.Dtos;
using ExamPlatform.Modules.Proctoring.Domain;
using ExamPlatform.Modules.Proctoring.Domain.Exceptions;
using ExamPlatform.Modules.Proctoring.Endpoints;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.Proctoring.UnitTests;

/// <summary>The scan: scores the finished attempts of an exam, keeps every decided flag as it was, and audits the run (FR-27).</summary>
public class RiskScanHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _examId = Guid.NewGuid();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IAttemptSignalSource _signals = Substitute.For<IAttemptSignalSource>();
    private readonly FakeRiskAssessmentRepository _assessments = new();
    private readonly IProctoringUnitOfWork _unitOfWork = Substitute.For<IProctoringUnitOfWork>();
    private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
    private readonly RunRiskScanHandler _handler;

    public RiskScanHandlerTests()
    {
        var clock = Substitute.For<Clock>();
        clock.UtcNow.Returns(Now);
        _catalog.FindAsync(_examId, Arg.Any<CancellationToken>()).Returns(new ExamSnapshot(
            _examId, "Maths Final", null, true, Now, Now.AddHours(3), null, null, 1, 0, 0, []));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(0);
        _handler = new RunRiskScanHandler(
            _catalog,
            _signals,
            _assessments,
            _unitOfWork,
            new RiskScoringOptions().ToPolicy(),
            clock,
            new ProctoringAuditTrail(_auditLogger, Substitute.For<IRequestContext>()));
    }

    private void FinishedAttempts(params AttemptRiskInputs[] attempts) =>
        _signals.ListFinishedAttemptsAsync(_examId, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<AttemptSignals>>(attempts.Select(ToSignals).ToList()));

    private AttemptSignals ToSignals(AttemptRiskInputs input) => new(
        input.AttemptId,
        _examId,
        input.CandidateId,
        input.AttemptNumber,
        input.StartedAtUtc,
        input.FinishedAtUtc,
        input.Invalidated,
        input.AnsweredCount,
        input.FocusDepartures,
        input.ClientChanges,
        input.WrongAnswers.Select(w => new WrongAnswer(w.QuestionId, w.ChoiceKey)).ToList());

    private AttemptRiskInputs Input(int focus = 0, params WrongAnswerKey[] wrong) =>
        RiskTestData.Attempt(examId: _examId, focus: focus, wrongAnswers: wrong);

    [Fact]
    public async Task AnUnknownExam_IsNotFound()
    {
        _catalog.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ExamSnapshot?)null);

        await Assert.ThrowsAsync<ExamNotFoundError>(() => _handler.HandleAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task AScan_ScoresEveryFinishedAttempt_AndCountsTheFlags()
    {
        FinishedAttempts(Input(focus: 3), Input(focus: 0));

        var result = await _handler.HandleAsync(_examId, CancellationToken.None);

        Assert.Equal(new RiskScanResultDto(_examId, Scored: 2, Flagged: 1, KeptDecided: 0), result);
        Assert.Equal(2, _assessments.Stored.Count);
        Assert.Single(_assessments.Stored, a => a.Flagged);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AScan_AuditsTheRun()
    {
        FinishedAttempts(Input(focus: 3));

        await _handler.HandleAsync(_examId, CancellationToken.None);

        await _auditLogger.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e => e.Action == "Proctoring.RiskScanRun" && e.EntityId == _examId.ToString()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AScan_LeavesADecidedFlagAsItWas_AndRescoresAnOpenOne()
    {
        var decidedInput = Input(focus: 3);
        var openInput = Input(focus: 0);
        FinishedAttempts(decidedInput, openInput);
        await _handler.HandleAsync(_examId, CancellationToken.None);
        var decided = _assessments.Stored.Single(a => a.AttemptId == decidedInput.AttemptId);
        decided.Dismiss(Guid.NewGuid(), "Confirmed with the centre", Now);

        // The open attempt gains departures since the last scan; the decided one gains more, which must not change its record.
        FinishedAttempts(decidedInput with { FocusDepartures = 9 }, openInput with { FocusDepartures = 3 });
        var result = await _handler.HandleAsync(_examId, CancellationToken.None);

        Assert.Equal(1, result.KeptDecided);
        Assert.Equal(1, result.Scored);
        Assert.Equal(RiskFlagStatus.Dismissed, decided.Status);
        Assert.Equal(30, decided.Score);
        var rescored = _assessments.Stored.Single(a => a.AttemptId == openInput.AttemptId);
        Assert.True(rescored.Flagged);
        Assert.Equal(30, rescored.Score);
    }

    [Fact]
    public async Task SharedWrongAnswers_AreCountedAcrossTheExam()
    {
        // Three identical rare wrong answers shared by two candidates: each scores the shared-answer signal (35), which flags on its own.
        var questions = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var wrong = questions.Select(q => new WrongAnswerKey(q, "opt-x")).ToArray();
        FinishedAttempts(Input(0, wrong), Input(0, wrong));

        var result = await _handler.HandleAsync(_examId, CancellationToken.None);

        Assert.Equal(2, result.Flagged);
        Assert.All(_assessments.Stored, a => Assert.Equal(35, a.Signals.Single(s => s.Kind == RiskSignalKind.SharedWrongAnswers).Points));
    }

    [Fact]
    public async Task AnExamWithNoFinishedAttempts_ScansNothing()
    {
        FinishedAttempts();

        var result = await _handler.HandleAsync(_examId, CancellationToken.None);

        Assert.Equal(new RiskScanResultDto(_examId, 0, 0, 0), result);
        Assert.Empty(_assessments.Stored);
    }
}
