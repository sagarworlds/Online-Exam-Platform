using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// How a saved or cleared answer loads its attempt: with the slim loader, the one question's answer and sequence number are enough, so the
/// whole attempt is not read; an attempt whose time has run out is read in full first, because closing it scores every answer.
/// </summary>
public class AnswerLoadingTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IAnswerAttemptLoader _loader = Substitute.For<IAnswerAttemptLoader>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();

    private readonly QuestionSnapshot _first = Fixtures.Question("First?");
    private readonly QuestionSnapshot _second = Fixtures.Question("Second?");
    private readonly ExamSnapshot _exam;

    public AnswerLoadingTests()
    {
        _exam = Fixtures.Exam([_first, _second]);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _first, _second }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
    }

    private Attempt OpenAttempt(DateTime deadline) => Attempt.Start(_exam.Id, _candidate, 1, Fixtures.Now.AddMinutes(-5), deadline);

    private AttemptAccess Access => new(
        _attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock, new ProctorPolicy(), _loader);

    private SaveAnswerHandler Save => new(Access, _bank, _unitOfWork, _clock);

    private ClearAnswerHandler Clear => new(Access, _unitOfWork, _clock);

    [Fact]
    public async Task ASave_LoadsTheAttemptWithTheSlimLoader_AndNeverReadsTheWholeAttempt()
    {
        var attempt = OpenAttempt(Fixtures.Now.AddMinutes(25));
        _loader.LoadForAnswerAsync(attempt.Id, _first.Id, Arg.Any<CancellationToken>()).Returns(attempt);

        await Save.HandleAsync(attempt.Id, _candidate, _first.Id, _first.Correct(), CancellationToken.None);

        Assert.Equal(_first.Id, Assert.Single(attempt.Answers).QuestionId);
        await _loader.Received(1).LoadForAnswerAsync(attempt.Id, _first.Id, Arg.Any<CancellationToken>());
        await _attempts.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATypedAnswer_UsesTheSlimLoaderToo()
    {
        var typed = Fixtures.TextQuestion("Paris");
        var exam = Fixtures.Exam([typed]);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([typed]);
        var attempt = Attempt.Start(exam.Id, _candidate, 1, Fixtures.Now.AddMinutes(-5), Fixtures.Now.AddMinutes(25));
        _loader.LoadForAnswerAsync(attempt.Id, typed.Id, Arg.Any<CancellationToken>()).Returns(attempt);

        await Save.HandleTextAsync(attempt.Id, _candidate, typed.Id, "Paris", CancellationToken.None);

        Assert.Equal("Paris", Assert.Single(attempt.Answers).AnswerText);
        await _attempts.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task AClear_LoadsTheAttemptWithTheSlimLoader_AndNeverReadsTheWholeAttempt()
    {
        var attempt = OpenAttempt(Fixtures.Now.AddMinutes(25));
        attempt.RecordAnswer(_first.Id, _first.Correct(), Fixtures.Now);
        _loader.LoadForAnswerAsync(attempt.Id, _first.Id, Arg.Any<CancellationToken>()).Returns(attempt);

        await Clear.HandleAsync(attempt.Id, _candidate, _first.Id, CancellationToken.None);

        Assert.Empty(attempt.Answers);
        await _attempts.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task AnAttemptThatIsNotTheCandidates_IsReportedAsNotFound_LikeAMissingOne()
    {
        var attempt = OpenAttempt(Fixtures.Now.AddMinutes(25));
        _loader.LoadForAnswerAsync(attempt.Id, _first.Id, Arg.Any<CancellationToken>()).Returns(attempt);

        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Save.HandleAsync(attempt.Id, Guid.NewGuid(), _first.Id, _first.Correct(), CancellationToken.None));
        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Save.HandleAsync(Guid.NewGuid(), _candidate, _first.Id, _first.Correct(), CancellationToken.None));
    }

    [Fact]
    public async Task AnAttemptWhoseTimeHasRunOut_IsReadInFull_ThenClosedWithEveryAnswerScored()
    {
        var attempt = OpenAttempt(Fixtures.Now.AddMinutes(10));
        attempt.RecordAnswer(_first.Id, _first.Correct(), Fixtures.Now);
        attempt.RecordAnswer(_second.Id, _second.Correct(), Fixtures.Now);
        _loader.LoadForAnswerAsync(attempt.Id, _first.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        _clock.UtcNow = Fixtures.Now.AddMinutes(11); // the deadline passed while the candidate was away

        await Assert.ThrowsAsync<AttemptNotInProgressError>(() => Save.HandleAsync(attempt.Id, _candidate, _first.Id, _first.Wrong(), CancellationToken.None));

        // Closing scores every answer, so the whole attempt was read first; the late save was refused and changed nothing.
        await _attempts.Received(1).GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>());
        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(attempt.MaxScore, attempt.Score);
        Assert.NotEqual(0m, attempt.Score);
    }

    [Fact]
    public async Task WithoutALoader_TheWholeAttemptIsRead_AsItAlwaysWas()
    {
        var attempt = OpenAttempt(Fixtures.Now.AddMinutes(25));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        var withoutLoader = new AttemptAccess(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock, new ProctorPolicy());

        await new SaveAnswerHandler(withoutLoader, _bank, _unitOfWork, _clock)
            .HandleAsync(attempt.Id, _candidate, _first.Id, _first.Correct(), CancellationToken.None);

        Assert.Single(attempt.Answers);
        await _attempts.Received(1).GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>());
        await _loader.DidNotReceiveWithAnyArgs().LoadForAnswerAsync(default, default, default);
    }
}
