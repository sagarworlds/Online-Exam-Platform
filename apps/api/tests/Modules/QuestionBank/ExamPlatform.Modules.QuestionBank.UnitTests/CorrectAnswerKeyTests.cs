using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>
/// Correcting an answer key after candidates have already answered (FR-31): the one deliberate exception to
/// <see cref="QuestionReviseTests"/>'s "answered means locked" rule.
/// </summary>
public class CorrectAnswerKeyTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    private static Question Capitals() =>
        Question.Create("Capital of France?", [new("Paris", true), new("Rome", false), new("Oslo", false)], Guid.NewGuid(), Now);

    private static Question MultiAnswer() =>
        Question.Create(
            "Which are prime?", [new("2", true), new("3", true), new("4", false), new("6", false)], Guid.NewGuid(), Now, allowsMultiple: true);

    [Fact]
    public void CorrectingTheKey_ChangesWhichOptionIsCorrect_EvenThoughRevisingWouldHaveBeenLocked()
    {
        var question = Capitals();
        var rome = question.Options[1].Id;

        var changed = question.CorrectAnswerKey([rome], Now.AddDays(1));

        Assert.True(changed);
        Assert.True(question.Options.Single(o => o.Id == rome).IsCorrect);
        Assert.Equal(1, question.Options.Count(o => o.IsCorrect));
        Assert.Equal(Now.AddDays(1), question.AnswerKeyCorrectedAtUtc);
    }

    [Fact]
    public void CorrectingTheKey_TouchesOnlyWhichOptionsAreCorrect_NeverTextOrderOrPinning()
    {
        var question = Capitals();
        var original = question.Options.Select(o => (o.Id, o.Text, o.Order, o.IsPinned)).ToList();
        var rome = question.Options[1].Id;

        question.CorrectAnswerKey([rome], Now);

        Assert.Equal(original, question.Options.Select(o => (o.Id, o.Text, o.Order, o.IsPinned)));
    }

    [Fact]
    public void CorrectingTheKeyToTheSameOptions_ChangesNothing_AndReportsNoChange()
    {
        var question = Capitals();
        var paris = question.Options[0].Id;

        var changed = question.CorrectAnswerKey([paris], Now);

        Assert.False(changed);
        Assert.Null(question.AnswerKeyCorrectedAtUtc);
    }

    [Fact]
    public void CorrectingWithAnOptionIdThatIsNotOneOfTheQuestions_IsRefused()
    {
        var question = Capitals();

        Assert.Throws<InvalidQuestionError>(() => question.CorrectAnswerKey([Guid.NewGuid()], Now));
    }

    [Fact]
    public void CorrectingASingleAnswerQuestion_ToZeroOrMoreThanOneCorrectOption_IsRefused()
    {
        var question = Capitals();

        Assert.Throws<InvalidQuestionError>(() => question.CorrectAnswerKey([], Now));
        Assert.Throws<InvalidQuestionError>(() =>
            question.CorrectAnswerKey(question.Options.Select(o => o.Id).ToList(), Now));
    }

    [Fact]
    public void CorrectingAMultipleAnswerQuestion_CanChangeWhichOptionsAreCorrect_WithinItsShapeRule()
    {
        var question = MultiAnswer();
        var two = question.Options[0].Id;
        var four = question.Options[2].Id;

        var changed = question.CorrectAnswerKey([two, four], Now);

        Assert.True(changed);
        Assert.Equal(new[] { two, four }.OrderBy(id => id), question.Options.Where(o => o.IsCorrect).Select(o => o.Id).OrderBy(id => id));
    }

    [Fact]
    public void CorrectingAMultipleAnswerQuestion_ToEveryOptionCorrect_IsRefused()
    {
        var question = MultiAnswer();

        Assert.Throws<InvalidQuestionError>(() => question.CorrectAnswerKey(question.Options.Select(o => o.Id).ToList(), Now));
    }
}
