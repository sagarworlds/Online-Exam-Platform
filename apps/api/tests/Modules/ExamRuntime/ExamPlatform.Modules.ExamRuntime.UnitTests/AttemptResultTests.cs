using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// The result page (FR-32): what it shows, who may read it, when it is refused, and how the rank is worked out from the exam's other
/// candidates. The rank rules themselves are in <see cref="ResultStandingTests"/>.
/// </summary>
public class AttemptResultTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();

    private readonly QuestionSnapshot _right = Fixtures.Question("Right one");
    private readonly QuestionSnapshot _wrong = Fixtures.Question("Wrong one");
    private readonly QuestionSnapshot _skipped = Fixtures.Question("Skipped one");

    // Negative marking, so a right answer, a wrong one and a skipped one each earn different marks.
    private ExamSnapshot ExamWith(ExamResultReleaseMode mode = ExamResultReleaseMode.Instant, DateTime? releaseTime = null, DateTime? end = null)
    {
        var exam = Fixtures.Exam(
            [_right, _wrong, _skipped], correct: 4m, incorrect: -1m, unattempted: -0.25m, resultRelease: mode, resultReleaseTime: releaseTime, end: end);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _right, _wrong, _skipped }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        return exam;
    }

    /// <summary>A submitted attempt: right on the first question, wrong on the second, the third skipped (score 4 - 1 - 0.25 = 2.75).</summary>
    private Attempt Submitted(ExamSnapshot exam, Guid? owner = null)
    {
        var attempt = Attempt.Start(exam.Id, owner ?? _candidate, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        attempt.RecordAnswer(_right.Id, _right.Correct(), Fixtures.Now);
        attempt.RecordAnswer(_wrong.Id, _wrong.Wrong(), Fixtures.Now);
        var questions = new[] { _right, _wrong, _skipped }.ToDictionary(q => q.Id);
        var result = AttemptScorer.Score(exam, questions, attempt.Answers.ToList());
        attempt.Submit(Fixtures.Now.AddMinutes(10), result.Score, result.MaxScore);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    private void OthersScored(params decimal[] bests) =>
        _attempts.ListBestScoresOfOtherCandidatesAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<decimal>>(bests));

    private AttemptAccess Access => new(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock);
    private GetAttemptResultHandler Handler => new(Access, new AttemptReviewBuilder(_bank, _clock), _attempts, _clock);

    public AttemptResultTests()
    {
        OthersScored();
    }

    [Fact]
    public async Task TheResult_CarriesTheScore_TheCounts_AndTheMarksBySection()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam);

        var result = await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(2.75m, result.Score);
        Assert.Equal(1, result.CorrectCount);
        Assert.Equal(1, result.WrongCount);
        Assert.Equal(1, result.UnansweredCount);
        var section = Assert.Single(result.Sections);
        Assert.Equal(2.75m, section.Score);
        Assert.Equal(1, section.CorrectCount);
        Assert.Equal(1, section.WrongCount);
        Assert.Equal(1, section.UnansweredCount);
    }

    [Fact]
    public async Task TheRank_IsWorkedOutFromTheOtherCandidatesBestResults()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam);
        OthersScored(5m, 1m, 2.75m);

        var result = await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        // One result is above 2.75, so this is second. The tied one counts in this candidate's favour: three of the four scored 2.75 or less.
        Assert.Equal(2, result.Rank);
        Assert.Equal(4, result.CohortSize);
        Assert.Equal(75m, result.Percentile);
    }

    [Fact]
    public async Task TheCohort_IsTheExamsOtherCandidates_NotThisCandidatesOwnAttempts()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam);

        await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        await _attempts.Received(1).ListBestScoresOfOtherCandidatesAsync(exam.Id, _candidate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhileTheWindowIsOpen_TheRankIsProvisional_AndAfterItCloses_ItIsFinal()
    {
        var exam = ExamWith(end: Fixtures.Now.AddHours(2));
        var attempt = Submitted(exam);

        Assert.True((await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None)).Provisional);

        _clock.UtcNow = Fixtures.Now.AddHours(3);
        Assert.False((await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None)).Provisional);
    }

    [Fact]
    public async Task BeforeTheResultIsReleased_TheResultIsRefused_WithTheReleaseTime()
    {
        var exam = ExamWith(ExamResultReleaseMode.Scheduled, releaseTime: Fixtures.Now.AddDays(1));
        var attempt = Submitted(exam);

        var error = await Assert.ThrowsAsync<ResultsNotReleasedError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));

        Assert.Equal("results_not_released", error.ErrorCode);
    }

    [Fact]
    public async Task AnOpenAttempt_HasNoResult()
    {
        var exam = ExamWith();
        var attempt = Attempt.Start(exam.Id, _candidate, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);

        await Assert.ThrowsAsync<AttemptNotSubmittedError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));
    }

    [Fact]
    public async Task AnInvalidatedAttempt_HasNoResult_BecauseItNoLongerCounts()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam);
        attempt.Invalidate(Guid.NewGuid(), "Shared answers with another candidate.", Fixtures.Now.AddDays(1));

        await Assert.ThrowsAsync<AttemptInvalidatedError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));
    }

    [Fact]
    public async Task SomeoneElsesAttempt_IsNotFound_ExactlyAsAMissingOneIs()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam, owner: Guid.NewGuid());

        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));
    }
}
