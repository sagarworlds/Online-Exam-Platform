using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// How an exam's candidates answered it, for item analysis (<see cref="IExamResponseReader"/>): only released attempts count, each question is
/// judged as the candidate saw it, and the answers are marked by the same scorer as the results.
/// </summary>
public class ExamResponseReaderTests
{
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();

    private readonly QuestionSnapshot _first = Fixtures.Question("First");
    private readonly QuestionSnapshot _second = Fixtures.Question("Second");

    private ExamResponseReader Reader => new(_attempts, _catalog, _bank, _clock);

    private ExamSnapshot ExamWith(ExamResultReleaseMode mode = ExamResultReleaseMode.Instant, DateTime? releaseTime = null)
    {
        var exam = Fixtures.Exam([_first, _second], correct: 4m, incorrect: -1m, unattempted: 0m, resultRelease: mode, resultReleaseTime: releaseTime);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _first, _second }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        return exam;
    }

    /// <summary>A submitted attempt by <paramref name="candidate"/>: right on the first question, wrong on the second.</summary>
    private Attempt Sitting(ExamSnapshot exam, Guid candidate)
    {
        var attempt = Attempt.Start(exam.Id, candidate, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        attempt.RecordAnswer(_first.Id, _first.Correct(), Fixtures.Now);
        attempt.RecordAnswer(_second.Id, _second.Wrong(), Fixtures.Now);
        var result = AttemptScorer.Score(exam, new Dictionary<Guid, QuestionSnapshot> { [_first.Id] = _first, [_second.Id] = _second }, attempt.Answers.ToList());
        attempt.Submit(Fixtures.Now.AddMinutes(10), result.Score, result.MaxScore);
        return attempt;
    }

    private void Counted(params Attempt[] attempts) =>
        _attempts.ListCountedForExamAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Attempt>>(attempts));

    [Fact]
    public async Task AnUnknownExam_IsNull()
    {
        _catalog.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ExamSnapshot?)null);

        Assert.Null(await Reader.ReadAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task HeldResults_CountNothing_AndSayThatTheyAreHeld()
    {
        var exam = ExamWith(ExamResultReleaseMode.Manual);
        Counted(Sitting(exam, Guid.NewGuid()));

        var responses = await Reader.ReadAsync(exam.Id, CancellationToken.None);

        Assert.NotNull(responses);
        Assert.False(responses.ResultsReleased);
        Assert.Empty(responses.Attempts);
        await _attempts.DidNotReceiveWithAnyArgs().ListCountedForExamAsync(default, default);
    }

    [Fact]
    public async Task EachCountedAttempt_SaysWhetherEachQuestionWasAnsweredFullyCorrectly()
    {
        var exam = ExamWith();
        var candidate = Guid.NewGuid();
        Counted(Sitting(exam, candidate));

        var responses = await Reader.ReadAsync(exam.Id, CancellationToken.None);

        Assert.NotNull(responses);
        Assert.True(responses.ResultsReleased);
        Assert.Equal(new[] { _first.Id, _second.Id }, responses.QuestionIds);
        var attempt = Assert.Single(responses.Attempts);
        Assert.Equal(candidate, attempt.CandidateId);
        // Right on the first question earns 4 and wrong on the second costs 1, so the attempt scored 3.
        Assert.Equal(3m, attempt.Score);
        Assert.Equal(new[] { new QuestionResponse(_first.Id, true), new QuestionResponse(_second.Id, false) }, attempt.Questions);
    }

    [Fact]
    public async Task AQuestionCorrectedAfterTheCandidateSatIt_IsJudgedAsItWasSat()
    {
        // The key was right at version 1, which the candidate answered; the bank now says the second option is correct (version 2). The
        // candidate's answer must still count as correct, because that is the key they sat.
        var exam = ExamWith();
        var v1 = _first with { VersionNumber = 1 };
        var correctedOptions = _first.Options.Select(o => o with { IsCorrect = !o.IsCorrect }).ToList();
        var v2 = _first with { VersionNumber = 2, Options = correctedOptions };
        var attempt = Attempt.Start(exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        attempt.PinQuestionVersions(new Dictionary<Guid, int> { [_first.Id] = 1, [_second.Id] = 1 });
        attempt.RecordAnswer(_first.Id, _first.Correct(), Fixtures.Now);
        attempt.Submit(Fixtures.Now.AddMinutes(10), 4m, 8m);
        Counted(attempt);
        _bank.GetVersionsAsync(Arg.Any<IReadOnlyCollection<QuestionVersionRef>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([v1, _second with { VersionNumber = 1 }]));
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([v2]));

        var responses = await Reader.ReadAsync(exam.Id, CancellationToken.None);

        var answered = Assert.Single(Assert.Single(responses!.Attempts).Questions, q => q.QuestionId == _first.Id);
        Assert.True(answered.Correct);
    }
}
