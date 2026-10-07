using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Scoring a submitted attempt again from the answers that are stored, for a result that disagrees with its paper.</summary>
public class RescoreAttemptTests
{
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IAttemptLock _lock = Substitute.For<IAttemptLock>();
    private readonly QuestionSnapshot _first = Fixtures.Question("First");
    private readonly QuestionSnapshot _second = Fixtures.Question("Second");
    private readonly ExamSnapshot _exam;

    public RescoreAttemptTests()
    {
        _exam = Fixtures.Exam([_first, _second]);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([_first, _second]));
        _unitOfWork.LockAttemptAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_lock);
    }

    private RescoreAttemptHandler Handler =>
        new(new StaffAttemptAccess(_attempts, _catalog, new AttemptAccess(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock)),
            _bank, _unitOfWork, _clock);

    /// <summary>A submitted attempt that answered both questions correctly, with the score it was stored with.</summary>
    private Attempt Submitted(decimal storedScore)
    {
        var attempt = Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now.AddHours(-1), Fixtures.Now.AddHours(1));
        attempt.RecordAnswer(_first.Id, _first.Correct(), Fixtures.Now.AddMinutes(-50));
        attempt.RecordAnswer(_second.Id, _second.Correct(), Fixtures.Now.AddMinutes(-49));
        attempt.Submit(Fixtures.Now.AddMinutes(-40), storedScore, 2);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    [Fact]
    public async Task AScoreThatDisagreesWithTheStoredAnswers_IsCorrected_WithARevisionTheCandidateCanSee()
    {
        // The case this exists for: the last answer was saved as the attempt was submitted, and the score missed it.
        var attempt = Submitted(storedScore: 1);

        var summary = await Handler.HandleAsync(_exam.Id, attempt.Id, "  Last answer was missed  ", CancellationToken.None);

        Assert.Equal(2, attempt.Score);
        Assert.Equal(2, summary.Score);
        var revision = Assert.Single(attempt.Revisions);
        Assert.Equal(1, revision.PreviousScore);
        Assert.Equal(2, revision.NewScore);
        Assert.Equal("Rescored by staff: Last answer was missed", revision.Reason);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _lock.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).LockAttemptAsync(attempt.Id, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AScoreThatAlreadyMatches_ChangesNothing_AndSavesNothing()
    {
        var attempt = Submitted(storedScore: 2);

        await Handler.HandleAsync(_exam.Id, attempt.Id, "Checking", CancellationToken.None);

        Assert.Empty(attempt.Revisions);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ARescoreNeedsAReason(string? reason)
    {
        var attempt = Submitted(storedScore: 1);

        await Assert.ThrowsAsync<InvalidAttemptError>(() => Handler.HandleAsync(_exam.Id, attempt.Id, reason, CancellationToken.None));

        Assert.Equal(1, attempt.Score);
    }

    [Fact]
    public async Task AnAttemptStillOpen_HasNoScoreToRedo()
    {
        var attempt = Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);

        await Assert.ThrowsAsync<AttemptNotSubmittedError>(() => Handler.HandleAsync(_exam.Id, attempt.Id, "Why not", CancellationToken.None));
    }

    [Fact]
    public async Task AnAttemptOfAnotherExam_IsNotFound()
    {
        var attempt = Submitted(storedScore: 1);

        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Handler.HandleAsync(Guid.NewGuid(), attempt.Id, "Wrong exam", CancellationToken.None));
    }
}
