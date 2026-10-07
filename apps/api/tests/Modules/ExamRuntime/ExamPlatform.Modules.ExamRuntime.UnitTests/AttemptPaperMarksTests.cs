using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Staff reading a result against the paper: the candidate's choice, the correct answer, the verdict and the marks of each question.</summary>
public class AttemptPaperMarksTests
{
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly QuestionSnapshot _right = Fixtures.Question("Answered correctly");
    private readonly QuestionSnapshot _wrong = Fixtures.Question("Answered wrongly");
    private readonly QuestionSnapshot _skipped = Fixtures.Question("Left blank");
    private readonly Attempt _attempt;
    private readonly Guid _examId;

    public AttemptPaperMarksTests()
    {
        var exam = Fixtures.Exam([_right, _wrong, _skipped]);
        _examId = exam.Id;
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>()
                .Select(id => new[] { _right, _wrong, _skipped }.FirstOrDefault(q => q.Id == id)).OfType<QuestionSnapshot>().ToList());

        _attempt = Attempt.Start(exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        _attempt.RecordAnswer(_right.Id, _right.Correct(), Fixtures.Now.AddMinutes(1));
        _attempt.RecordAnswer(_wrong.Id, _wrong.Wrong(), Fixtures.Now.AddMinutes(2));
        _attempts.GetByIdAsync(_attempt.Id, Arg.Any<CancellationToken>()).Returns(_attempt);
    }

    private GetAttemptPaperHandler Handler => new(_catalog, _attempts, _bank);

    private async Task<AttemptPaperQuestionDto> QuestionAsync(Guid id)
    {
        var paper = await Handler.HandleAsync(_examId, _attempt.Id, CancellationToken.None);
        return paper.Sections.SelectMany(s => s.Questions).Single(q => q.Id == id);
    }

    [Fact]
    public async Task ASubmittedAttempt_ShowsEachQuestionsVerdictAndMarks_AndTheTotals()
    {
        _attempt.Submit(Fixtures.Now.AddMinutes(10), 1, 3);

        var paper = await Handler.HandleAsync(_examId, _attempt.Id, CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, paper.Status);
        Assert.Equal(1, paper.Score);
        Assert.Equal(3, paper.MaxScore);
        var questions = paper.Sections.SelectMany(s => s.Questions).ToDictionary(q => q.Id);
        Assert.Equal(AnswerVerdict.Correct, questions[_right.Id].Verdict);
        Assert.Equal(AnswerVerdict.Wrong, questions[_wrong.Id].Verdict);
        Assert.Equal(AnswerVerdict.Unanswered, questions[_skipped.Id].Verdict);
        Assert.True(questions[_right.Id].Marks > 0);
        Assert.True(questions[_wrong.Id].Marks <= 0);
    }

    [Fact]
    public async Task EveryOption_ShowsWhetherItIsCorrect_AndWhetherTheCandidateChoseIt()
    {
        _attempt.Submit(Fixtures.Now.AddMinutes(10), 1, 3);

        var wrong = await QuestionAsync(_wrong.Id);

        Assert.Equal(_wrong.Options.Count, wrong.Options!.Count);
        Assert.Equal(_wrong.Options.Where(o => o.IsCorrect).Select(o => o.Id), wrong.Options.Where(o => o.IsCorrect).Select(o => o.Id));
        var chosen = Assert.Single(wrong.Options, o => o.WasChosen);
        Assert.Equal(_wrong.Wrong(), chosen.Id);
        Assert.False(chosen.IsCorrect);
        Assert.DoesNotContain(await QuestionAsync(_skipped.Id) is { Options: { } o } ? o : [], x => x.WasChosen);
    }

    [Fact]
    public async Task AnAttemptStillInProgress_ShowsWhatHasBeenChosenSoFar_ButNoVerdictOrMarks()
    {
        var paper = await Handler.HandleAsync(_examId, _attempt.Id, CancellationToken.None);

        Assert.Equal(AttemptStatus.InProgress, paper.Status);
        Assert.Null(paper.Score);
        var right = paper.Sections.SelectMany(s => s.Questions).Single(q => q.Id == _right.Id);
        Assert.Null(right.Verdict);
        Assert.Null(right.Marks);
        Assert.Contains(right.Options!, o => o.WasChosen && o.IsCorrect);
    }

    [Fact]
    public async Task AQuestionTheBankNoLongerHas_IsListedWithoutOptions_AndTheRestOfThePaperStillShows()
    {
        _attempt.Submit(Fixtures.Now.AddMinutes(10), 1, 3);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>()
                .Select(id => new[] { _right, _skipped }.FirstOrDefault(q => q.Id == id)).OfType<QuestionSnapshot>().ToList());

        var gone = await QuestionAsync(_wrong.Id);
        var kept = await QuestionAsync(_right.Id);

        Assert.Null(gone.Text);
        Assert.Null(gone.Options);
        Assert.Null(gone.Verdict);
        Assert.Equal(AnswerVerdict.Correct, kept.Verdict);
    }

    [Fact]
    public async Task AnInvalidatedAttempt_IsFlagged_ForStaffWhoStillSeeItsMarks()
    {
        _attempt.Submit(Fixtures.Now.AddMinutes(10), 1, 3);
        _attempt.Invalidate(Guid.NewGuid(), "Copied answers", Fixtures.Now.AddMinutes(20));

        var paper = await Handler.HandleAsync(_examId, _attempt.Id, CancellationToken.None);

        Assert.True(paper.IsInvalidated);
        Assert.Equal(1, paper.Score);
    }

    [Fact]
    public async Task TheQuestionsComeInTheOrderTheCandidateSawThem_WhenTheExamShufflesQuestions()
    {
        // From the second attempt on, questions are shuffled; the paper must number them as the candidate saw them.
        var exam = Fixtures.Exam([_right, _wrong, _skipped]) with { ShuffleQuestions = true };
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        var attempt = Attempt.Start(exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);

        var paper = await Handler.HandleAsync(exam.Id, attempt.Id, CancellationToken.None);

        var expected = AttemptOrdering.Arrange(exam.Sections[0].QuestionIds, id => id, attempt.Id, attempt.Number, exam.Sections[0].Id, authorShuffles: true);
        Assert.Equal(expected, paper.Sections[0].Questions.Select(q => q.Id));
    }
}
