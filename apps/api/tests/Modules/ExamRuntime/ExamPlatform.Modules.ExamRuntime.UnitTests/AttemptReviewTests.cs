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
/// The answer review (FR-32, FR-33): which answers were right, shown only for a finished attempt, only to its owner, and only
/// once the exam's author has released the answers.
/// </summary>
public class AttemptReviewTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IDisputeRepository _disputes = Substitute.For<IDisputeRepository>();

    private readonly QuestionSnapshot _right = Fixtures.Question("Right one");
    private readonly QuestionSnapshot _wrong = Fixtures.Question("Wrong one");
    private readonly QuestionSnapshot _skipped = Fixtures.Question("Skipped one");

    // Negative marking, so a wrong answer, a skipped one and a right one all earn different marks.
    private ExamSnapshot ExamWith(ExamResultReleaseMode mode = ExamResultReleaseMode.Instant, DateTime? releaseTime = null)
    {
        var exam = Fixtures.Exam([_right, _wrong, _skipped], correct: 4m, incorrect: -1m, unattempted: -0.25m, resultRelease: mode, resultReleaseTime: releaseTime);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _right, _wrong, _skipped }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        return exam;
    }

    private Attempt Open(ExamSnapshot exam, Guid? owner = null, DateTime? deadline = null)
    {
        var attempt = Attempt.Start(exam.Id, owner ?? _candidate, 1, Fixtures.Now, deadline ?? Fixtures.Now.AddMinutes(30));
        attempt.RecordAnswer(_right.Id, _right.Correct(), Fixtures.Now);
        attempt.RecordAnswer(_wrong.Id, _wrong.Wrong(), Fixtures.Now);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    private Attempt Submitted(ExamSnapshot exam, Guid? owner = null)
    {
        var attempt = Open(exam, owner);
        var questions = new[] { _right, _wrong, _skipped }.ToDictionary(q => q.Id);
        var result = AttemptScorer.Score(exam, questions, attempt.Answers.ToList());
        attempt.Submit(Fixtures.Now.AddMinutes(10), result.Score, result.MaxScore);
        return attempt;
    }

    private AttemptReviewBuilder Builder => new(_bank, _clock);
    private AttemptAccess Access => new(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock);
    private GetAttemptReviewHandler Handler => new(Access, Builder, _disputes, new DisputePolicy(7), _clock);

    private static ReviewQuestionDto Question(AttemptReviewDto review, string text) =>
        review.Sections.SelectMany(s => s.Questions).Single(q => q.Text == text);

    public AttemptReviewTests()
    {
        _disputes.ListForAttemptAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    // ---- what the review says ---------------------------------------------------------------------------------

    [Fact]
    public async Task AResultNeverRevised_IsVersionOne_WithNoRevisions()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam);

        var review = await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(1, review.ResultVersion);
        Assert.Empty(review.Revisions!);
    }

    [Fact]
    public async Task EachRevision_IsNumberedByTheVersionItProduced_AndTheResultIsTheLatest()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam);
        attempt.ReviseScore(3.75m, 12m, "Q1's key was corrected", Fixtures.Now.AddDays(1));
        attempt.ReviseScore(6.75m, 12m, "Q2's key was corrected", Fixtures.Now.AddDays(2));

        var review = await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(3, review.ResultVersion);
        Assert.Equal([2, 3], review.Revisions!.Select(r => r.Version));
        Assert.Equal(6.75m, review.Score);
    }

    [Fact]
    public async Task TheReview_SaysWhetherTheKeyCanStillBeDisputed_AndUntilWhen()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam);

        var review = await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.NotNull(review.DisputeWindow);
        Assert.True(review.DisputeWindow.Enabled);
        Assert.True(review.DisputeWindow.Open);
        Assert.Equal(attempt.SubmittedAtUtc!.Value.AddDays(7), review.DisputeWindow.ClosesAtUtc);
    }

    [Fact]
    public async Task TheReview_ListsTheCandidatesOwnDisputes_WithHowStaffAnsweredEach()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam);
        var open = Dispute.Raise(attempt.Id, exam.Id, _candidate, _wrong.Id, "Both options are right", Fixtures.Now.AddMinutes(11));
        var answered = Dispute.Raise(attempt.Id, exam.Id, _candidate, _right.Id, "The key is wrong", Fixtures.Now.AddMinutes(12));
        answered.Reject(Guid.NewGuid(), Fixtures.Now.AddHours(1), "The key stands");
        _disputes.ListForAttemptAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns([open, answered]);

        var review = await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(2, review.Disputes!.Count);
        Assert.Equal((DisputeStatus.Open, _wrong.Id), (review.Disputes[0].Status, review.Disputes[0].QuestionId));
        Assert.Equal(DisputeStatus.Rejected, review.Disputes[1].Status);
        Assert.Equal("The key stands", review.Disputes[1].ResolutionNote);
    }

    [Fact]
    public async Task TheReview_MarksEveryOption_AsRightAndAsChosen_AndEveryQuestionWithItsVerdictAndMarks()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam);

        var review = await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        var right = Question(review, "Right one");
        Assert.Equal(AnswerVerdict.Correct, right.Verdict);
        Assert.Equal(4m, right.Marks);
        Assert.Equal(_right.Correct(), Assert.Single(right.Options, o => o.IsCorrect).Id);
        Assert.Equal(_right.Correct(), Assert.Single(right.Options, o => o.WasChosen).Id);

        var wrong = Question(review, "Wrong one");
        Assert.Equal(AnswerVerdict.Wrong, wrong.Verdict);
        Assert.Equal(-1m, wrong.Marks);
        Assert.Equal(_wrong.Correct(), Assert.Single(wrong.Options, o => o.IsCorrect).Id);
        Assert.Equal(_wrong.Wrong(), Assert.Single(wrong.Options, o => o.WasChosen).Id);

        var skipped = Question(review, "Skipped one");
        Assert.Equal(AnswerVerdict.Unanswered, skipped.Verdict);
        Assert.Equal(-0.25m, skipped.Marks);
        Assert.DoesNotContain(skipped.Options, o => o.WasChosen);
        Assert.Contains(skipped.Options, o => o.IsCorrect);
    }

    [Fact]
    public async Task TheReview_CountsTheVerdicts_AndItsQuestionMarksAddUpToTheStoredScore()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam);

        var review = await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal((1, 1, 1), (review.CorrectCount, review.WrongCount, review.UnansweredCount));
        Assert.Equal(attempt.Score, review.Score);
        Assert.Equal(attempt.MaxScore, review.MaxScore);
        Assert.Equal(review.Score, review.Sections.SelectMany(s => s.Questions).Sum(q => q.Marks));
        Assert.Equal(2.75m, review.Score);
    }

    [Fact]
    public async Task AnAttemptThatRanOutOfTime_IsClosedWhenItsReviewIsAskedFor_AndSaysItWasAutoSubmitted()
    {
        var exam = ExamWith();
        var attempt = Open(exam, deadline: Fixtures.Now.AddMinutes(5));
        _clock.UtcNow = Fixtures.Now.AddMinutes(20);

        var review = await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.True(review.AutoSubmitted);
        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(1, review.CorrectCount);
    }

    // ---- what it refuses --------------------------------------------------------------------------------------

    [Fact]
    public async Task AnOpenAttempt_HasNoReview_AndTheAnswerKeyIsNotEvenRead()
    {
        var exam = ExamWith();
        var attempt = Open(exam);

        await Assert.ThrowsAsync<AttemptNotSubmittedError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));

        await _bank.DidNotReceive().GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SomeoneElsesAttempt_LooksExactlyLikeAMissingOne()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam, owner: Guid.NewGuid());

        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));
        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Handler.HandleAsync(Guid.NewGuid(), _candidate, CancellationToken.None));
        await _bank.DidNotReceive().GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeforeAScheduledRelease_TheReviewIsRefused_SaysFromWhen_AndTheKeyIsNotRead()
    {
        var releaseAt = Fixtures.Now.AddDays(1);
        var attempt = Submitted(ExamWith(ExamResultReleaseMode.Scheduled, releaseAt));

        var error = await Assert.ThrowsAsync<ResultsNotReleasedError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));

        Assert.Equal("results_not_released", error.ErrorCode);
        Assert.Equal(409, error.HttpStatusCode);
        Assert.Contains("2026-10-03 09:00 UTC", error.Message);
        // Submitted() scores without the bank, so any read here would be the review fetching the key it must not show yet.
        await _bank.DidNotReceive().GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AScheduledRelease_OpensAtItsInstant()
    {
        var releaseAt = Fixtures.Now.AddDays(1);
        var attempt = Submitted(ExamWith(ExamResultReleaseMode.Scheduled, releaseAt));
        _clock.UtcNow = releaseAt.AddTicks(-1);
        await Assert.ThrowsAsync<ResultsNotReleasedError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));

        _clock.UtcNow = releaseAt;
        var review = await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(attempt.Id, review.AttemptId);
    }

    [Fact]
    public async Task AManualRelease_IsHeldBack_ThenOpenWhenTheAdministratorReleasesIt()
    {
        var held = Submitted(ExamWith(ExamResultReleaseMode.Manual));
        var error = await Assert.ThrowsAsync<ResultsNotReleasedError>(() => Handler.HandleAsync(held.Id, _candidate, CancellationToken.None));
        Assert.Contains("organiser", error.Message);

        var released = Submitted(ExamWith(ExamResultReleaseMode.Manual, Fixtures.Now.AddMinutes(-1)));
        var review = await Handler.HandleAsync(released.Id, _candidate, CancellationToken.None);

        Assert.Equal(released.Id, review.AttemptId);
    }

    // ---- what the attempt itself says about it ----------------------------------------------------------------

    [Fact]
    public async Task AnOpenAttempt_ReportsNoReview_AtAll()
    {
        var exam = ExamWith();
        var dto = await new AttemptViewBuilder(_bank, _clock).BuildAsync(Open(exam), exam, CancellationToken.None);

        Assert.Null(dto.Review);
    }

    [Fact]
    public async Task ASubmittedAttempt_SaysWhetherItsReviewIsOpen_AndFromWhenIfThatIsKnown()
    {
        var releaseAt = Fixtures.Now.AddDays(1);
        var instant = ExamWith();
        var instantDto = await new AttemptViewBuilder(_bank, _clock).BuildAsync(Submitted(instant), instant, CancellationToken.None);
        var scheduled = ExamWith(ExamResultReleaseMode.Scheduled, releaseAt);
        var scheduledDto = await new AttemptViewBuilder(_bank, _clock).BuildAsync(Submitted(scheduled), scheduled, CancellationToken.None);
        var manual = ExamWith(ExamResultReleaseMode.Manual);
        var manualDto = await new AttemptViewBuilder(_bank, _clock).BuildAsync(Submitted(manual), manual, CancellationToken.None);

        Assert.True(instantDto.Review!.Available);
        Assert.False(scheduledDto.Review!.Available);
        Assert.Equal(releaseAt, scheduledDto.Review.AvailableFromUtc);
        Assert.False(manualDto.Review!.Available);
        Assert.Equal(ExamResultReleaseMode.Manual, manualDto.Review.Mode);
        Assert.Null(manualDto.Review.AvailableFromUtc);
        // Even with the answers held back, a submitted attempt shows no questions: the score is all it carries.
        Assert.Empty(scheduledDto.Sections);
    }

    [Fact]
    public void TheDtosUsedWhileSittingTheExam_HaveNoPlaceForTheAnswerKey()
    {
        // The key may only travel in AttemptReviewDto. If a property naming it is ever added to the types a candidate
        // reads while taking the exam, this fails before any test of behaviour could notice.
        var sittingTypes = new[] { typeof(AttemptDto), typeof(AttemptSectionDto), typeof(AttemptQuestionDto), typeof(AttemptOptionDto), typeof(MyExamDto), typeof(AttemptReviewAvailabilityDto) };

        var offending = sittingTypes
            .SelectMany(t => t.GetProperties().Select(p => $"{t.Name}.{p.Name}"))
            .Where(name => name.Contains("Correct", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("CorrectCount", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offending);
    }
}
