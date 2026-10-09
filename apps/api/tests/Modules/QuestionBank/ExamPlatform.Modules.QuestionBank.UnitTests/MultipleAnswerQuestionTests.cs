using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>Questions with more than one correct option: what shape they may have, and what stays fixed once candidates have answered.</summary>
public class MultipleAnswerQuestionTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);

    private static Question Primes() =>
        Question.Create("Which are prime?", [new("2", true), new("3", true), new("4", false)], Guid.NewGuid(), Now, allowsMultiple: true);

    private static List<QuestionOptionEdit> Unchanged(Question question) =>
        question.Options.Select(o => new QuestionOptionEdit(o.Id, o.Text, o.IsCorrect)).ToList();

    [Fact]
    public void AQuestionIsSingleAnswer_UnlessItSaysOtherwise()
    {
        var question = Question.Create("Q?", [new("A", true), new("B", false)], Guid.NewGuid(), Now);

        Assert.False(question.AllowsMultiple);
    }

    [Fact]
    public void AMultipleAnswerQuestion_MayHaveSeveralCorrectOptions()
    {
        var question = Primes();

        Assert.True(question.AllowsMultiple);
        Assert.Equal(2, question.Options.Count(o => o.IsCorrect));
    }

    [Fact]
    public void AMultipleAnswerQuestion_MayHaveJustOneCorrectOption()
    {
        var question = Question.Create("Q?", [new("A", true), new("B", false), new("C", false)], Guid.NewGuid(), Now, allowsMultiple: true);

        Assert.Single(question.Options, o => o.IsCorrect);
    }

    [Fact]
    public void AMultipleAnswerQuestion_NeedsAtLeastOneCorrectOption()
    {
        var error = Assert.Throws<InvalidQuestionError>(() =>
            Question.Create("Q?", [new("A", false), new("B", false)], Guid.NewGuid(), Now, allowsMultiple: true));

        Assert.Contains("at least one correct", error.Message);
    }

    [Fact]
    public void AMultipleAnswerQuestion_CannotHaveEveryOptionCorrect_BecauseNobodyCouldGetItWrong()
    {
        Assert.Throws<InvalidQuestionError>(() =>
            Question.Create("Q?", [new("A", true), new("B", true)], Guid.NewGuid(), Now, allowsMultiple: true));
    }

    [Fact]
    public void ASingleAnswerQuestion_StillNeedsExactlyOneCorrectOption()
    {
        var error = Assert.Throws<InvalidQuestionError>(() =>
            Question.Create("Q?", [new("A", true), new("B", true), new("C", false)], Guid.NewGuid(), Now));

        Assert.Equal("Exactly one option must be marked correct.", error.Message);
    }

    [Fact]
    public void BeforeAnyoneAnswers_AQuestionCanBecomeMultipleAnswer_AndBackAgain()
    {
        var question = Question.Create("Q?", [new("A", true), new("B", false), new("C", false)], Guid.NewGuid(), Now);
        var edits = question.Options.Select(o => new QuestionOptionEdit(o.Id, o.Text, o.Text != "C")).ToList();

        question.Revise("Q?", edits, answered: false, allowsMultiple: true, nowUtc: Now);
        Assert.True(question.AllowsMultiple);

        question.Revise("Q?", Unchanged(question).Select((e, i) => e with { IsCorrect = i == 0 }).ToList(), answered: false, allowsMultiple: false, nowUtc: Now);
        Assert.False(question.AllowsMultiple);
    }

    [Fact]
    public void OnceAnswered_TheWordingMayChange()
    {
        var question = Primes();

        question.Revise("Which are prime numbers?", Unchanged(question), answered: true, allowsMultiple: true, nowUtc: Now);

        Assert.Equal("Which are prime numbers?", question.Text);
        Assert.True(question.AllowsMultiple);
    }

    [Fact]
    public void OnceAnswered_ASingleAnswerQuestionCannotBecomeMultipleAnswer_EvenWithTheSameCorrectOption()
    {
        var question = Question.Create("Q?", [new("A", true), new("B", false), new("C", false)], Guid.NewGuid(), Now);

        Assert.Throws<QuestionLockedError>(() => question.Revise("Q?", Unchanged(question), answered: true, allowsMultiple: true, nowUtc: Now));

        Assert.False(question.AllowsMultiple);
    }

    [Fact]
    public void OnceAnswered_ChangingTheCorrectSet_IsStillLocked()
    {
        var question = Primes();
        var flipped = Unchanged(question).Select((e, i) => e with { IsCorrect = i != 1 }).ToList();

        Assert.Throws<QuestionLockedError>(() => question.Revise("Q?", flipped, answered: true, allowsMultiple: true, nowUtc: Now));
    }
}
