using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>
/// Text-answer questions: what shape they take, what their accepted answers must be, and what stays fixed once candidates have answered.
/// Their accepted answers are their answer key, so they follow the same rules the options of a multiple-choice question do.
/// </summary>
public class TextAnswerQuestionTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);

    private static Question Capital(params string?[] accepted) =>
        Question.Create("Capital of France?", null, Guid.NewGuid(), Now, isTextAnswer: true, acceptedAnswers: accepted);

    [Fact]
    public void ATextQuestion_KeepsItsAcceptedAnswers_TrimmedAndWithBlanksDropped()
    {
        var question = Capital("  Paris ", "", "city of paris");

        Assert.True(question.IsTextAnswer);
        Assert.Equal(new[] { "Paris", "city of paris" }, question.AcceptedAnswers);
        Assert.Empty(question.Options);
    }

    [Fact]
    public void ATextQuestion_NeedsAtLeastOneAcceptedAnswer()
    {
        var error = Assert.Throws<InvalidQuestionError>(() => Capital("  ", null));

        Assert.Contains("at least one accepted answer", error.Message);
    }

    [Fact]
    public void ATextQuestion_CannotHaveOptions()
    {
        Assert.Throws<InvalidQuestionError>(() =>
            Question.Create("Q?", [new("A", true), new("B", false)], Guid.NewGuid(), Now, isTextAnswer: true, acceptedAnswers: ["A"]));
    }

    [Fact]
    public void ATextQuestion_CannotAllowSeveralCorrectAnswers()
    {
        Assert.Throws<InvalidQuestionError>(() =>
            Question.Create("Q?", null, Guid.NewGuid(), Now, allowsMultiple: true, isTextAnswer: true, acceptedAnswers: ["A"]));
    }

    [Fact]
    public void ATextQuestion_RefusesTwoAcceptedAnswersThatMeanTheSame()
    {
        // The same comparison the exam runtime marks with, so two answers that would both match one typed answer are refused here.
        var error = Assert.Throws<InvalidQuestionError>(() => Capital("Paris", "  paris "));

        Assert.Contains("more than once", error.Message);
    }

    [Fact]
    public void ATextQuestion_RefusesMoreAcceptedAnswersThanTheMaximum()
    {
        var answers = Enumerable.Range(1, Question.MaxAcceptedAnswers + 1).Select(i => $"answer {i}").ToArray();

        Assert.Throws<InvalidQuestionError>(() => Capital(answers));
    }

    [Fact]
    public void ATextQuestion_RefusesAnAcceptedAnswerLongerThanTheMaximum() =>
        Assert.Throws<InvalidQuestionError>(() => Capital(new string('x', Question.MaxAcceptedAnswerLength + 1)));

    [Fact]
    public void AMultipleChoiceQuestion_CannotCarryAcceptedAnswers()
    {
        Assert.Throws<InvalidQuestionError>(() =>
            Question.Create("Q?", [new("A", true), new("B", false)], Guid.NewGuid(), Now, acceptedAnswers: ["A"]));
    }

    [Fact]
    public void AQuestionIsMultipleChoice_UnlessItSaysOtherwise()
    {
        var question = Question.Create("Q?", [new("A", true), new("B", false)], Guid.NewGuid(), Now);

        Assert.False(question.IsTextAnswer);
        Assert.Empty(question.AcceptedAnswers);
    }

    [Fact]
    public void ATextQuestion_HasNoOptionKeyToCorrect()
    {
        var question = Capital("Paris");

        Assert.Throws<InvalidQuestionError>(() => question.CorrectAnswerKey([], Now));
    }

    [Fact]
    public void CorrectingTheAcceptedAnswers_ReplacesThemAndRecordsWhen()
    {
        var question = Capital("Paris");
        var later = Now.AddDays(1);

        var changed = question.CorrectAcceptedAnswers(["Paris", "City of Paris"], later);

        Assert.True(changed);
        Assert.Equal(new[] { "Paris", "City of Paris" }, question.AcceptedAnswers);
        Assert.Equal(later, question.AnswerKeyCorrectedAtUtc);
    }

    [Fact]
    public void CorrectingTheAcceptedAnswers_AddsAVersion_SoAReviewCanShowTheKeyThatWasUsed()
    {
        var question = Capital("Paris");

        question.CorrectAcceptedAnswers(["Paris", "Lutetia"], Now.AddDays(1));

        Assert.Equal(new[] { "Paris", "Lutetia" }, question.Versions.Last().AcceptedAnswers);
        Assert.Equal(new[] { "Paris" }, question.Versions[0].AcceptedAnswers);
    }

    [Fact]
    public void CorrectingTheAcceptedAnswers_WithTheSameAnswers_ChangesNothing()
    {
        var question = Capital("Paris");
        var versions = question.Versions.Count;

        var changed = question.CorrectAcceptedAnswers(["  PARIS "], Now.AddDays(1));

        Assert.False(changed);
        Assert.Equal(versions, question.Versions.Count);
    }

    [Fact]
    public void CorrectingTheAcceptedAnswers_IsRefusedForAMultipleChoiceQuestion()
    {
        var question = Question.Create("Q?", [new("A", true), new("B", false)], Guid.NewGuid(), Now);

        Assert.Throws<InvalidQuestionError>(() => question.CorrectAcceptedAnswers(["A"], Now));
    }

    [Fact]
    public void BeforeAnyoneAnswers_AQuestionCanBecomeATextQuestion()
    {
        var question = Question.Create("Q?", [new("A", true), new("B", false)], Guid.NewGuid(), Now);

        question.Revise("Q?", [], answered: false, nowUtc: Now, isTextAnswer: true, acceptedAnswers: ["A"]);

        Assert.True(question.IsTextAnswer);
        Assert.Empty(question.Options);
        Assert.Equal(new[] { "A" }, question.AcceptedAnswers);
    }

    [Fact]
    public void OnceAnswered_AQuestionCannotBecomeATextQuestion()
    {
        var question = Question.Create("Q?", [new("A", true), new("B", false)], Guid.NewGuid(), Now);

        Assert.Throws<QuestionLockedError>(() =>
            question.Revise("Q?", [], answered: true, nowUtc: Now, isTextAnswer: true, acceptedAnswers: ["A"]));

        Assert.False(question.IsTextAnswer);
    }

    [Fact]
    public void OnceAnswered_TheAcceptedAnswersCannotChange()
    {
        var question = Capital("Paris");

        Assert.Throws<QuestionLockedError>(() =>
            question.Revise("Capital of France?", [], answered: true, nowUtc: Now, isTextAnswer: true, acceptedAnswers: ["Paris", "Lutetia"]));

        Assert.Equal(new[] { "Paris" }, question.AcceptedAnswers);
    }

    [Fact]
    public void OnceAnswered_TheWordingOfATextQuestionMayStillChange()
    {
        var question = Capital("Paris");

        question.Revise("What is the capital of France?", [], answered: true, nowUtc: Now, isTextAnswer: true, acceptedAnswers: ["Paris"]);

        Assert.Equal("What is the capital of France?", question.Text);
        Assert.True(question.IsTextAnswer);
    }
}
