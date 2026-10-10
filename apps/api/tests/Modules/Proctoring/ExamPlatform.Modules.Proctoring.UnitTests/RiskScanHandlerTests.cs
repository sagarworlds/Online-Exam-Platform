using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Proctoring.Application;
using ExamPlatform.Modules.Proctoring.Application.Commands;
using ExamPlatform.Modules.Proctoring.Application.Dtos;
using ExamPlatform.Modules.Proctoring.Domain;
using ExamPlatform.Modules.Proctoring.Domain.Exceptions;
using ExamPlatform.Modules.Proctoring.Endpoints;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.Proctoring.UnitTests;

/// <summary>
/// The scan: scores the finished attempts of an exam that the minors policy allows, keeps every decided flag as it was, audits the run, and
/// refuses an overlapping write as a structured conflict (FR-27, section 7.2).
/// </summary>
public class RiskScanHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _examId = Guid.NewGuid();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IAttemptSignalSource _signals = Substitute.For<IAttemptSignalSource>();
    private readonly ICandidateAgeDirectory _ages = Substitute.For<ICandidateAgeDirectory>();
    private readonly FakeRiskAssessmentRepository _assessments = new();
    private readonly IProctoringUnitOfWork _unitOfWork = Substitute.For<IProctoringUnitOfWork>();
    private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
    private readonly Dictionary<Guid, AttemptSignals> _finished = new();
    private readonly Clock _clock = Substitute.For<Clock>();
    private RunRiskScanHandler _handler = null!;

    public RiskScanHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _catalog.FindAsync(_examId, Arg.Any<CancellationToken>()).Returns(new ExamSnapshot(
            _examId, "Maths Final", null, true, Now, Now.AddHours(3), null, null, 1, 0, 0, []));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(0);
        NoMinors();
        Build(minorsScanEnabled: false);
    }

    /// <summary>Builds the handler with the minors switch set as the test needs it.</summary>
    private void Build(bool minorsScanEnabled)
    {
        var minorPolicy = new MinorScanPolicy(minorsScanEnabled);
        _handler = new RunRiskScanHandler(
            _catalog,
            _signals,
            new AttemptScanScope(_signals, _ages, minorPolicy),
            _assessments,
            _unitOfWork,
            new RiskScoringOptions().ToPolicy(),
            _clock,
            new ProctoringAuditTrail(_auditLogger, Substitute.For<IRequestContext>()));
    }

    /// <summary>Makes the age directory report exactly these attempts as sat by a minor.</summary>
    private void MinorAttempts(params Guid[] attemptIds) =>
        _ages.FindAttemptsSatAsMinorAsync(Arg.Any<IReadOnlyCollection<AttemptStart>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<Guid>>(attemptIds.ToHashSet()));

    private void NoMinors() => MinorAttempts();

    /// <summary>Sets the finished attempts the exam runtime reports, and answers the scoped reads from them.</summary>
    private void FinishedAttempts(params AttemptRiskInputs[] attempts)
    {
        _finished.Clear();
        foreach (var input in attempts)
        {
            _finished[input.AttemptId] = ToSignals(input);
        }

        _signals.ListFinishedAttemptRefsAsync(_examId, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<FinishedAttemptRef>>(attempts.Select(a => new FinishedAttemptRef(a.AttemptId, a.CandidateId, a.StartedAtUtc)).ToList()));
        _signals.ListFinishedAttemptsAsync(_examId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call =>
            Task.FromResult<IReadOnlyList<AttemptSignals>>(
                call.ArgAt<IReadOnlyCollection<Guid>>(1).Where(_finished.ContainsKey).Select(id => _finished[id]).ToList()));
    }

    private static AttemptSignals ToSignals(AttemptRiskInputs input) => new(
        input.AttemptId,
        input.ExamId,
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

        Assert.Equal(new RiskScanResultDto(_examId, Scored: 2, Flagged: 1, KeptDecided: 0, ExcludedUnder18: 0), result);
        Assert.Equal(2, _assessments.Stored.Count);
        Assert.Single(_assessments.Stored, a => a.Flagged);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AScan_AuditsTheRun_IncludingHowManyWereLeftOut()
    {
        FinishedAttempts(Input(focus: 3));

        await _handler.HandleAsync(_examId, CancellationToken.None);

        await _auditLogger.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e => e.Action == "Proctoring.RiskScanRun" && e.EntityId == _examId.ToString() && e.Metadata["excludedUnder18"] == "0"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithTheSwitchOff_AnUnder18Attempt_IsExcludedAndCounted_AndItsAnswersAreNotRead()
    {
        // The attempt of the minor is left out before any of its answers are read: only the adult's attempt is asked for.
        var adult = Input(focus: 0);
        var minor = Input(focus: 3);
        FinishedAttempts(adult, minor);
        MinorAttempts(minor.AttemptId);

        var result = await _handler.HandleAsync(_examId, CancellationToken.None);

        Assert.Equal(new RiskScanResultDto(_examId, Scored: 1, Flagged: 0, KeptDecided: 0, ExcludedUnder18: 1), result);
        await _signals.Received(1).ListFinishedAttemptsAsync(
            _examId,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(adult.AttemptId)),
            Arg.Any<CancellationToken>());
        Assert.DoesNotContain(_assessments.Stored, a => a.AttemptId == minor.AttemptId);
    }

    [Fact]
    public async Task WithTheSwitchOn_AnUnder18Attempt_IsScanned_AndNothingIsExcluded()
    {
        var minor = Input(focus: 3);
        FinishedAttempts(minor);
        MinorAttempts(minor.AttemptId);
        Build(minorsScanEnabled: true);

        var result = await _handler.HandleAsync(_examId, CancellationToken.None);

        Assert.Equal(new RiskScanResultDto(_examId, Scored: 1, Flagged: 1, KeptDecided: 0, ExcludedUnder18: 0), result);
        Assert.Single(_assessments.Stored, a => a.AttemptId == minor.AttemptId && a.Flagged);
        // With minors allowed there is nothing to exclude, so the age directory is not even asked.
        await _ages.DidNotReceive().FindAttemptsSatAsMinorAsync(Arg.Any<IReadOnlyCollection<AttemptStart>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithTheSwitchOff_WhenNoAttemptIsByAMinor_ExcludesNothing()
    {
        FinishedAttempts(Input(focus: 0), Input(focus: 3));

        var result = await _handler.HandleAsync(_examId, CancellationToken.None);

        Assert.Equal(0, result.ExcludedUnder18);
        Assert.Equal(2, result.Scored);
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

        Assert.Equal(new RiskScanResultDto(_examId, 0, 0, 0, 0), result);
        Assert.Empty(_assessments.Stored);
    }

    [Fact]
    public async Task AScanWhoseSaveIsRefusedAsOverlapping_PropagatesTheConflict_AndIsNotAudited()
    {
        // The other scan wrote this attempt first. This scan's save is refused as a whole, so it must not record that a scan ran.
        FinishedAttempts(Input(focus: 3));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new ScanAlreadyRunningError(new Exception("conflict"))));

        var error = await Assert.ThrowsAsync<ScanAlreadyRunningError>(() => _handler.HandleAsync(_examId, CancellationToken.None));

        Assert.Equal(409, error.HttpStatusCode);
        Assert.Equal("scan_already_running", error.ErrorCode);
        await _auditLogger.DidNotReceive().RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }
}
