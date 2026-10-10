using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// What other modules may read of a candidate's results (<see cref="ICandidateResultReader"/>): only their own, only released ones, marked the
/// same way the result page marks them.
/// </summary>
public class CandidateResultReaderTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();

    private readonly QuestionSnapshot _right = Fixtures.Question("Right one");
    private readonly QuestionSnapshot _wrong = Fixtures.Question("Wrong one");
    private readonly QuestionSnapshot _skipped = Fixtures.Question("Skipped one");

    // Negative marking, so a right answer, a wrong one and a skipped one each earn different marks.
    private ExamSnapshot ExamWith(ExamResultReleaseMode mode = ExamResultReleaseMode.Instant, DateTime? releaseTime = null)
    {
        var exam = Fixtures.Exam(
            [_right, _wrong, _skipped], correct: 4m, incorrect: -1m, unattempted: -0.25m, resultRelease: mode, resultReleaseTime: releaseTime);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _right, _wrong, _skipped }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        return exam;
    }

    /// <summary>A submitted attempt: right on the first question, wrong on the second, the third skipped (score 4 - 1 - 0.25 = 2.75 of 12).</summary>
    private Attempt Submitted(ExamSnapshot exam)
    {
        var attempt = Attempt.Start(exam.Id, _candidate, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        attempt.RecordAnswer(_right.Id, _right.Correct(), Fixtures.Now);
        attempt.RecordAnswer(_wrong.Id, _wrong.Wrong(), Fixtures.Now);
        var result = AttemptScorer.Score(exam, new[] { _right, _wrong, _skipped }.ToDictionary(q => q.Id), attempt.Answers.ToList());
        attempt.Submit(Fixtures.Now.AddMinutes(10), result.Score, result.MaxScore);
        return attempt;
    }

    private void CountedForCandidate(params Attempt[] attempts) =>
        _attempts.ListCountedForCandidateAsync(_candidate, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Attempt>>(attempts));

    private CandidateResultReader Reader => new(_attempts, _catalog, _bank, _clock);

    [Fact]
    public async Task AReleasedResult_CarriesTheScore_AndHowTheAnswersFellInEachSection()
    {
        var exam = ExamWith();
        CountedForCandidate(Submitted(exam));

        var results = await Reader.ListReleasedAsync(_candidate, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(exam.Name, result.ExamName);
        Assert.Equal(2.75m, result.Score);
        Assert.Equal(12m, result.MaxScore);
        var section = Assert.Single(result.Sections);
        Assert.Equal(2.75m, section.Score);
        Assert.Equal(1, section.CorrectCount);
        Assert.Equal(1, section.WrongCount);
        Assert.Equal(0, section.PartialCount);
        Assert.Equal(1, section.UnansweredCount);
    }

    [Fact]
    public async Task AResultTheAuthorHasNotReleased_IsLeftOut_NotReportedAsZero()
    {
        // Manual release with no release time yet: how the candidate did is not known to anyone until the author releases it.
        var exam = ExamWith(ExamResultReleaseMode.Manual);
        CountedForCandidate(Submitted(exam));

        var results = await Reader.ListReleasedAsync(_candidate, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task AScheduledRelease_IsLeftOutUntilItsTime_AndIncludedAfterIt()
    {
        var exam = ExamWith(ExamResultReleaseMode.Scheduled, releaseTime: Fixtures.Now.AddHours(1));
        CountedForCandidate(Submitted(exam));

        Assert.Empty(await Reader.ListReleasedAsync(_candidate, CancellationToken.None));

        _clock.UtcNow = Fixtures.Now.AddHours(2);

        Assert.Single(await Reader.ListReleasedAsync(_candidate, CancellationToken.None));
    }

    [Fact]
    public async Task OnlyTheNamedCandidatesAttemptsAreAskedFor()
    {
        ExamWith();
        CountedForCandidate();

        await Reader.ListReleasedAsync(_candidate, CancellationToken.None);

        await _attempts.Received(1).ListCountedForCandidateAsync(_candidate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADrawnPaper_IsMarkedOnlyOnTheQuestionsOnIt()
    {
        // The candidate was drawn only the first question, so the skipped and the wrong question were never on their paper and must not
        // count as unanswered or wrong.
        var exam = ExamWith();
        var attempt = Attempt.Start(exam.Id, _candidate, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        attempt.SetPaper([(exam.Sections[0].Id, _right.Id)]);
        attempt.RecordAnswer(_right.Id, _right.Correct(), Fixtures.Now);
        var paper = exam.For(attempt);
        var result = AttemptScorer.Score(paper, new Dictionary<Guid, QuestionSnapshot> { [_right.Id] = _right }, attempt.Answers.ToList());
        attempt.Submit(Fixtures.Now.AddMinutes(10), result.Score, result.MaxScore);
        CountedForCandidate(attempt);

        var section = Assert.Single(Assert.Single(await Reader.ListReleasedAsync(_candidate, CancellationToken.None)).Sections);

        Assert.Equal(1, section.CorrectCount);
        Assert.Equal(0, section.WrongCount);
        Assert.Equal(0, section.UnansweredCount);
    }

    [Fact]
    public async Task AnExamThatCannotBeRead_IsRefused_NotSkipped()
    {
        // A result that silently disappeared would look like a candidate who never sat the exam.
        var exam = ExamWith();
        CountedForCandidate(Submitted(exam));
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns((ExamSnapshot?)null);

        await Assert.ThrowsAsync<ExamContentUnavailableError>(() => Reader.ListReleasedAsync(_candidate, CancellationToken.None));
    }

    [Fact]
    public async Task NoCountedAttempts_GivesNoResults()
    {
        CountedForCandidate();

        Assert.Empty(await Reader.ListReleasedAsync(_candidate, CancellationToken.None));
    }
}
