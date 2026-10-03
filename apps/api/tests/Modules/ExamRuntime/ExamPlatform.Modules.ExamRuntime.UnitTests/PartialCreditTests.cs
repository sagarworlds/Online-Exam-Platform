using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Partial credit on a multiple-answer question: a share of the marks for what was right, less what was wrong.</summary>
public class PartialCreditTests
{
    private static readonly QuestionSnapshot Primes = Fixtures.MultiQuestion();
    private static readonly ExamSnapshot Off = Fixtures.Exam([Primes], correct: 4m, incorrect: -1m, unattempted: 0.5m);
    private static readonly ExamSnapshot On = Off with { PartialCredit = true };

    private static QuestionMark Mark(ExamSnapshot exam, QuestionSnapshot question, params Guid[] chosen) => AttemptScorer.Mark(exam, question, chosen);

    [Fact]
    public void OneOfTwoCorrectOptions_EarnsHalfTheMarks_WhenPartialCreditIsOn()
    {
        Assert.Equal(new QuestionMark(AnswerVerdict.Partial, 2m), Mark(On, Primes, Primes.CorrectSet()[0]));
    }

    [Fact]
    public void TheSameAnswer_IsWrong_WhenPartialCreditIsOff()
    {
        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark(Off, Primes, Primes.CorrectSet()[0]));
    }

    [Fact]
    public void AWrongChoice_CancelsARightOne_SoTickingAtRandomDoesNotPay()
    {
        var oneRightOneWrong = new[] { Primes.CorrectSet()[0], Primes.Wrong() };

        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark(On, Primes, oneRightOneWrong));
    }

    [Fact]
    public void EveryCorrectOptionPlusOneWrong_StillEarnsAShare()
    {
        var tooMany = Primes.CorrectSet().Append(Primes.Wrong()).ToArray();

        Assert.Equal(new QuestionMark(AnswerVerdict.Partial, 2m), Mark(On, Primes, tooMany));
    }

    [Fact]
    public void OnlyWrongOptions_AreWrong_AndNothingChosen_IsUnanswered()
    {
        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark(On, Primes, Primes.Wrong()));
        Assert.Equal(new QuestionMark(AnswerVerdict.Unanswered, 0.5m), Mark(On, Primes));
    }

    [Fact]
    public void TheExactSet_IsStillFullMarks()
    {
        Assert.Equal(new QuestionMark(AnswerVerdict.Correct, 4m), Mark(On, Primes, Primes.CorrectSet()));
    }

    [Fact]
    public void ASingleAnswerQuestion_IsNeverPartial()
    {
        var single = Fixtures.Question();

        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark(On, single, single.Wrong()));
    }

    [Fact]
    public void AShareIsRoundedToTheHundredth()
    {
        var question = new QuestionSnapshot(Guid.NewGuid(), "Pick the evens",
        [
            new QuestionOptionSnapshot(Guid.NewGuid(), "2", IsCorrect: true),
            new QuestionOptionSnapshot(Guid.NewGuid(), "4", IsCorrect: true),
            new QuestionOptionSnapshot(Guid.NewGuid(), "6", IsCorrect: true),
            new QuestionOptionSnapshot(Guid.NewGuid(), "7", IsCorrect: false),
        ], AllowsMultiple: true);

        Assert.Equal(new QuestionMark(AnswerVerdict.Partial, 1.33m), Mark(On, question, question.CorrectSet()[0]));
    }

    [Fact]
    public void TheMaximumScoreIsUnchanged_SoPartialCreditNeverRaisesTheCeiling()
    {
        var score = AttemptScorer.Score(On, new Dictionary<Guid, QuestionSnapshot> { [Primes.Id] = Primes }, []);

        Assert.Equal(4m, score.MaxScore);
    }
}
