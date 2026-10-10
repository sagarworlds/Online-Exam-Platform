using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// The explanation of a question (FR-33) is part of the answer review only: it is shown once the exam's author has released the results,
/// and never before, because the review is the only thing built from a question's answer key and explanation.
/// </summary>
public class AttemptReviewExplanationTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();

    private ExamSnapshot ExamWith(QuestionSnapshot question, ExamResultReleaseMode mode, DateTime? releaseTime = null) =>
        Fixtures.Exam([question], resultRelease: mode, resultReleaseTime: releaseTime);

    private void BankHolds(QuestionSnapshot question) =>
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([question]));

    /// <summary>A submitted attempt that answered the question correctly.</summary>
    private static Attempt Submitted(ExamSnapshot exam, QuestionSnapshot question, Guid candidate)
    {
        var attempt = Attempt.Start(exam.Id, candidate, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        attempt.RecordAnswer(question.Id, question.Correct(), Fixtures.Now);
        attempt.Submit(Fixtures.Now.AddMinutes(10), 1m, 1m);
        return attempt;
    }

    [Fact]
    public async Task OnceReleased_TheReviewShowsEachQuestionsExplanation()
    {
        var question = Fixtures.Question("Capital of France?") with { Explanation = "Paris has been the capital for centuries." };
        var exam = ExamWith(question, ExamResultReleaseMode.Instant);
        BankHolds(question);
        var attempt = Submitted(exam, question, _candidate);

        var review = await new AttemptReviewBuilder(_bank, _clock).BuildAsync(attempt, exam, CancellationToken.None);

        var shown = Assert.Single(review.Sections.SelectMany(s => s.Questions));
        Assert.Equal("Paris has been the capital for centuries.", shown.Explanation);
    }

    [Fact]
    public async Task AQuestionWithNoExplanation_ShowsNone()
    {
        var question = Fixtures.Question("Capital of France?");
        var exam = ExamWith(question, ExamResultReleaseMode.Instant);
        BankHolds(question);
        var attempt = Submitted(exam, question, _candidate);

        var review = await new AttemptReviewBuilder(_bank, _clock).BuildAsync(attempt, exam, CancellationToken.None);

        Assert.Null(Assert.Single(review.Sections.SelectMany(s => s.Questions)).Explanation);
    }

    [Fact]
    public async Task BeforeTheRelease_NoReviewIsBuilt_SoNoExplanationCanLeave()
    {
        var question = Fixtures.Question("Capital of France?") with { Explanation = "Held back." };
        var exam = ExamWith(question, ExamResultReleaseMode.Scheduled, releaseTime: Fixtures.Now.AddDays(2));
        BankHolds(question);
        var attempt = Submitted(exam, question, _candidate);

        await Assert.ThrowsAsync<ResultsNotReleasedError>(() => new AttemptReviewBuilder(_bank, _clock).BuildAsync(attempt, exam, CancellationToken.None));
    }
}
