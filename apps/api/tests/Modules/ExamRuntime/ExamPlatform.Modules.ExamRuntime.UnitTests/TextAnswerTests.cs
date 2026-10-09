using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// Marking a text question: a typed answer is right when it matches an accepted answer, is never partly right, and a blank one is unanswered.
/// </summary>
public class TextAnswerScoringTests
{
    private static readonly QuestionSnapshot Capital = Fixtures.TextQuestion("Paris", "City of Paris");
    private static readonly ExamSnapshot Exam = Fixtures.Exam([Capital], correct: 2m, incorrect: -1m, unattempted: 0m);

    private static QuestionMark Mark(string? typed) => AttemptScorer.Mark(Exam, Capital, Array.Empty<Guid>(), typed);

    [Fact]
    public void AnAcceptedAnswer_IsRight() => Assert.Equal(new QuestionMark(AnswerVerdict.Correct, 2m), Mark("Paris"));

    [Fact]
    public void TheCaseAndSpacingOfAnAcceptedAnswer_DoNotMatter() =>
        Assert.Equal(new QuestionMark(AnswerVerdict.Correct, 2m), Mark("  city   OF paris "));

    [Fact]
    public void AnyOtherAnswer_IsWrong() => Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark("Lyon"));

    [Fact]
    public void APartOfAnAcceptedAnswer_IsWrong() => Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark("Par"));

    [Fact]
    public void ABlankAnswer_IsUnanswered() => Assert.Equal(new QuestionMark(AnswerVerdict.Unanswered, 0m), Mark("   "));

    [Fact]
    public void NoAnswer_IsUnanswered() => Assert.Equal(new QuestionMark(AnswerVerdict.Unanswered, 0m), Mark(null));

    [Fact]
    public void TheScore_CountsATypedAnswerLikeAnyOther()
    {
        var single = Fixtures.Question();
        var exam = Fixtures.Exam([Capital, single], correct: 2m, incorrect: -1m, unattempted: 0m);
        var attempt = Attempt.Start(exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(Capital.Id, "paris", Fixtures.Now);
        attempt.RecordAnswer(single.Id, single.Wrong(), Fixtures.Now);

        var score = AttemptScorer.Score(
            exam, new Dictionary<Guid, QuestionSnapshot> { [Capital.Id] = Capital, [single.Id] = single }, attempt.Answers.ToList());

        Assert.Equal(new AttemptScore(1m, 4m), score);
    }
}

/// <summary>The attempt's rules for a typed answer: stored as typed, one answer per question, and switching kind replaces the other.</summary>
public class TextAnswerAttemptTests
{
    private static Attempt Open() => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));

    [Fact]
    public void ATypedAnswer_IsKeptTrimmed_AndSavingAgainReplacesIt()
    {
        var attempt = Open();
        var question = Guid.NewGuid();

        attempt.RecordTextAnswer(question, "  Paris ", Fixtures.Now);
        attempt.RecordTextAnswer(question, "Lyon", Fixtures.Now);

        var answer = Assert.Single(attempt.Answers);
        Assert.Equal("Lyon", answer.AnswerText);
        Assert.Empty(answer.SelectedOptionIds);
    }

    [Fact]
    public void ABlankTypedAnswer_IsRefused_ToTakeAnAnswerBackIsToClearIt()
    {
        var attempt = Open();

        Assert.Throws<InvalidAttemptError>(() => attempt.RecordTextAnswer(Guid.NewGuid(), "   ", Fixtures.Now));

        Assert.Empty(attempt.Answers);
    }

    [Fact]
    public void ATypedAnswerLongerThanTheLimit_IsRefused()
    {
        var attempt = Open();

        Assert.Throws<InvalidAttemptError>(() =>
            attempt.RecordTextAnswer(Guid.NewGuid(), new string('x', TypedAnswer.MaxLength + 1), Fixtures.Now));
    }

    [Fact]
    public void ChoosingAnOptionAfterTyping_DropsTheTypedAnswer()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var option = Guid.NewGuid();

        attempt.RecordTextAnswer(question, "Paris", Fixtures.Now);
        attempt.RecordAnswer(question, option, Fixtures.Now);

        var answer = Assert.Single(attempt.Answers);
        Assert.Null(answer.AnswerText);
        Assert.Equal(new[] { option }, answer.SelectedOptionIds);
    }

    [Fact]
    public void TypingAfterChoosingAnOption_DropsTheChoice()
    {
        var attempt = Open();
        var question = Guid.NewGuid();

        attempt.RecordAnswer(question, Guid.NewGuid(), Fixtures.Now);
        attempt.RecordTextAnswer(question, "Paris", Fixtures.Now);

        var answer = Assert.Single(attempt.Answers);
        Assert.Equal("Paris", answer.AnswerText);
        Assert.Empty(answer.SelectedOptionIds);
    }

    [Fact]
    public void ATypedAnswer_CannotBeSavedOnceTheAttemptIsSubmitted()
    {
        var attempt = Open();
        attempt.Submit(Fixtures.Now, score: 0m, maxScore: 1m);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.RecordTextAnswer(Guid.NewGuid(), "Paris", Fixtures.Now));
    }
}

/// <summary>The save-answer handler for a typed answer: only a text question in the exam takes one, and a choice is refused for it.</summary>
public class TextAnswerHandlerTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();

    private readonly QuestionSnapshot _capital = Fixtures.TextQuestion("Paris");
    private readonly QuestionSnapshot _single = Fixtures.Question();
    private readonly ExamSnapshot _exam;
    private readonly Attempt _attempt;

    public TextAnswerHandlerTests()
    {
        _exam = Fixtures.Exam([_capital, _single]);
        _attempt = Attempt.Start(_exam.Id, _candidate, 1, Fixtures.Now.AddMinutes(-5), Fixtures.Now.AddMinutes(25));
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _attempts.GetByIdAsync(_attempt.Id, Arg.Any<CancellationToken>()).Returns(_attempt);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _capital, _single }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
    }

    private SaveAnswerHandler Save => new(new AttemptAccess(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock), _bank, _unitOfWork, _clock);

    [Fact]
    public async Task ATypedAnswer_IsSavedForATextQuestion()
    {
        await Save.HandleTextAsync(_attempt.Id, _candidate, _capital.Id, "paris", CancellationToken.None);

        Assert.Equal("paris", Assert.Single(_attempt.Answers).AnswerText);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATypedAnswer_IsRefusedForAMultipleChoiceQuestion()
    {
        await Assert.ThrowsAsync<InvalidAnswerError>(() =>
            Save.HandleTextAsync(_attempt.Id, _candidate, _single.Id, "4", CancellationToken.None));

        Assert.Empty(_attempt.Answers);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AChoiceOfOptions_IsRefusedForATextQuestion()
    {
        await Assert.ThrowsAsync<InvalidAnswerError>(() =>
            Save.HandleAsync(_attempt.Id, _candidate, _capital.Id, new[] { Guid.NewGuid() }, CancellationToken.None));

        Assert.Empty(_attempt.Answers);
    }

    [Fact]
    public async Task ABlankTypedAnswer_IsRefused()
    {
        await Assert.ThrowsAsync<InvalidAttemptError>(() =>
            Save.HandleTextAsync(_attempt.Id, _candidate, _capital.Id, "  ", CancellationToken.None));

        Assert.Empty(_attempt.Answers);
    }

    [Fact]
    public async Task ATypedAnswerToAQuestionOutsideTheExam_IsRefused()
    {
        await Assert.ThrowsAsync<InvalidAnswerError>(() =>
            Save.HandleTextAsync(_attempt.Id, _candidate, Guid.NewGuid(), "Paris", CancellationToken.None));

        Assert.Empty(_attempt.Answers);
    }
}
