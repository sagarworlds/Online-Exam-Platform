using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Marking a multiple-answer question: all or nothing, so only exactly the correct set earns the marks.</summary>
public class MultipleAnswerScoringTests
{
    private static readonly QuestionSnapshot Primes = Fixtures.MultiQuestion();
    private static readonly ExamSnapshot Exam = Fixtures.Exam([Primes], correct: 4m, incorrect: -1m, unattempted: 0.5m);

    private static QuestionMark Mark(params Guid[] chosen) => AttemptScorer.Mark(Exam, Primes, chosen);

    [Fact]
    public void ExactlyTheCorrectOptions_AreRight_InAnyOrder()
    {
        var correct = Primes.CorrectSet();

        Assert.Equal(new QuestionMark(AnswerVerdict.Correct, 4m), Mark(correct));
        Assert.Equal(new QuestionMark(AnswerVerdict.Correct, 4m), Mark(correct.Reverse().ToArray()));
    }

    [Fact]
    public void SomeOfTheCorrectOptions_AreWrong_BecausePartlyRightIsNotWhatWasAsked()
    {
        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark(Primes.CorrectSet()[0]));
    }

    [Fact]
    public void TheCorrectOptionsPlusAWrongOne_AreWrong()
    {
        var tooMany = Primes.CorrectSet().Append(Primes.Wrong()).ToArray();

        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark(tooMany));
    }

    [Fact]
    public void OnlyWrongOptions_AreWrong() => Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark(Primes.Wrong()));

    [Fact]
    public void NoOptions_IsUnanswered() => Assert.Equal(new QuestionMark(AnswerVerdict.Unanswered, 0.5m), Mark());

    [Fact]
    public void AnOptionTheQuestionDoesNotHave_FailsLoudly() =>
        Assert.Throws<ExamContentUnavailableError>(() => Mark(Primes.CorrectSet().Append(Guid.NewGuid()).ToArray()));

    [Fact]
    public void ASingleAnswerQuestion_IsMarkedByTheSameRule()
    {
        var single = Fixtures.Question();
        var exam = Fixtures.Exam([single]);

        Assert.Equal(AnswerVerdict.Correct, AttemptScorer.Mark(exam, single, new[] { single.Correct() }).Verdict);
        Assert.Equal(AnswerVerdict.Wrong, AttemptScorer.Mark(exam, single, new[] { single.Wrong() }).Verdict);
        Assert.Equal(AnswerVerdict.Wrong, AttemptScorer.Mark(exam, single, new[] { single.Correct(), single.Wrong() }).Verdict);
    }

    [Fact]
    public void TheScoreAddsUpEveryQuestion_AWholeMultiAnswerQuestionCountsOnce()
    {
        var single = Fixtures.Question();
        var exam = Fixtures.Exam([Primes, single], correct: 4m, incorrect: -1m, unattempted: 0m);
        var attempt = Attempt.Start(exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordAnswer(Primes.Id, Primes.CorrectSet(), Fixtures.Now);
        attempt.RecordAnswer(single.Id, single.Correct(), Fixtures.Now);

        var score = AttemptScorer.Score(exam, new Dictionary<Guid, QuestionSnapshot> { [Primes.Id] = Primes, [single.Id] = single }, attempt.Answers.ToList());

        Assert.Equal(new AttemptScore(8m, 8m), score);
    }
}

/// <summary>Saving the options chosen for a question: a set for a multiple-answer one, exactly one for a single-answer one.</summary>
public class MultipleAnswerAttemptTests
{
    private static Attempt Open() => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));

    [Fact]
    public void ASetOfOptions_IsKeptAsTheAnswer_AndSavingAgainReplacesTheWholeSet()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        attempt.RecordAnswer(question, [a, b], Fixtures.Now);
        attempt.RecordAnswer(question, [c], Fixtures.Now);

        Assert.Equal(new[] { c }, Assert.Single(attempt.Answers).SelectedOptionIds);
    }

    [Fact]
    public void RepeatedOptions_AreKeptOnce()
    {
        var attempt = Open();
        var option = Guid.NewGuid();

        attempt.RecordAnswer(Guid.NewGuid(), [option, option], Fixtures.Now);

        Assert.Equal(new[] { option }, Assert.Single(attempt.Answers).SelectedOptionIds);
    }

    [Fact]
    public void AnEmptySet_IsRefused_ToTakeAnAnswerBackIsToClearIt()
    {
        var attempt = Open();

        Assert.Throws<InvalidAttemptError>(() => attempt.RecordAnswer(Guid.NewGuid(), Array.Empty<Guid>(), Fixtures.Now));

        Assert.Empty(attempt.Answers);
    }

    [Fact]
    public void TheFirstOptionChosen_IsStillAvailableToReadersOfSingleAnswers()
    {
        var attempt = Open();
        var option = Guid.NewGuid();

        attempt.RecordAnswer(Guid.NewGuid(), option, Fixtures.Now);

        Assert.Equal(option, Assert.Single(attempt.Answers).SelectedOptionId);
    }
}

/// <summary>The save-answer handler's checks for sets of options, and what a candidate and a review are shown.</summary>
public class MultipleAnswerHandlerTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();

    private readonly QuestionSnapshot _multi = Fixtures.MultiQuestion();
    private readonly QuestionSnapshot _single = Fixtures.Question();
    private readonly ExamSnapshot _exam;
    private readonly Attempt _attempt;

    public MultipleAnswerHandlerTests()
    {
        _exam = Fixtures.Exam([_multi, _single]);
        _attempt = Attempt.Start(_exam.Id, _candidate, 1, Fixtures.Now.AddMinutes(-5), Fixtures.Now.AddMinutes(25));
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _attempts.GetByIdAsync(_attempt.Id, Arg.Any<CancellationToken>()).Returns(_attempt);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _multi, _single }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
    }

    private SaveAnswerHandler Save => new(new AttemptAccess(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock), _bank, _unitOfWork, _clock);

    [Fact]
    public async Task ASetOfOptions_IsSavedForAMultipleAnswerQuestion()
    {
        await Save.HandleAsync(_attempt.Id, _candidate, _multi.Id, _multi.CorrectSet(), CancellationToken.None);

        Assert.Equal(_multi.CorrectSet().Order(), Assert.Single(_attempt.Answers).SelectedOptionIds.Order());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OneOption_IsAValidAnswerToAMultipleAnswerQuestion()
    {
        await Save.HandleAsync(_attempt.Id, _candidate, _multi.Id, _multi.CorrectSet()[0], CancellationToken.None);

        Assert.Single(Assert.Single(_attempt.Answers).SelectedOptionIds);
    }

    [Fact]
    public async Task SeveralOptions_AreRefusedForASingleAnswerQuestion()
    {
        await Assert.ThrowsAsync<InvalidAnswerError>(() =>
            Save.HandleAsync(_attempt.Id, _candidate, _single.Id, new[] { _single.Correct(), _single.Wrong() }, CancellationToken.None));

        Assert.Empty(_attempt.Answers);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoOptions_AreRefused()
    {
        await Assert.ThrowsAsync<InvalidAnswerError>(() =>
            Save.HandleAsync(_attempt.Id, _candidate, _multi.Id, Array.Empty<Guid>(), CancellationToken.None));
    }

    [Fact]
    public async Task AnOptionFromAnotherQuestion_IsRefused_EvenAmongValidOnes()
    {
        await Assert.ThrowsAsync<InvalidAnswerError>(() =>
            Save.HandleAsync(_attempt.Id, _candidate, _multi.Id, new[] { _multi.CorrectSet()[0], _single.Correct() }, CancellationToken.None));

        Assert.Empty(_attempt.Answers);
    }

    [Fact]
    public async Task TheCandidatesViewShowsTheShapeOfTheQuestion_AndEveryOptionChosen()
    {
        _attempt.RecordAnswer(_multi.Id, _multi.CorrectSet(), Fixtures.Now);
        var views = new AttemptViewBuilder(_bank, _clock);

        var dto = await views.BuildAsync(_attempt, _exam, CancellationToken.None);

        var questions = dto.Sections.Single().Questions.ToDictionary(q => q.Id);
        Assert.True(questions[_multi.Id].AllowsMultiple);
        Assert.False(questions[_single.Id].AllowsMultiple);
        Assert.Equal(_multi.CorrectSet().Order(), questions[_multi.Id].SelectedOptionIds!.Order());
        Assert.Empty(questions[_single.Id].SelectedOptionIds!);
        Assert.Null(questions[_single.Id].SelectedOptionId);
    }

    [Fact]
    public async Task TheReviewMarksEveryChosenOption_AndTheVerdictIsAllOrNothing()
    {
        _attempt.RecordAnswer(_multi.Id, [_multi.CorrectSet()[0]], Fixtures.Now);
        _attempt.Submit(Fixtures.Now, 0m, 2m);
        var review = await new AttemptReviewBuilder(_bank, _clock).BuildAsync(_attempt, _exam, CancellationToken.None);

        var question = review.Sections.Single().Questions.Single(q => q.Id == _multi.Id);

        Assert.True(question.AllowsMultiple);
        Assert.Equal(AnswerVerdict.Wrong, question.Verdict);
        Assert.Equal(1, question.Options.Count(o => o.WasChosen));
        Assert.Equal(2, question.Options.Count(o => o.IsCorrect));
    }
}
