using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>
/// How strictly a typed answer is compared (FR-5) is part of a text question's answer key: it is stored with the question and with each
/// version, cannot change once candidates have answered except by a correction, and a question written before it existed stays exact.
/// </summary>
public class TextMatchRuleQuestionTests
{
    private static readonly DateTime Now = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);

    private static Question Capital(TextMatchRule rule, params string?[] accepted) =>
        Question.Create("Capital of France?", null, Guid.NewGuid(), Now, isTextAnswer: true, acceptedAnswers: accepted, matchRule: rule);

    [Fact]
    public void ATextQuestion_IsExactUnlessItSaysOtherwise_SoEveryOlderQuestionMarksAsItAlwaysDid()
    {
        var question = Question.Create("Capital of France?", null, Guid.NewGuid(), Now, isTextAnswer: true, acceptedAnswers: ["Paris"]);

        Assert.Equal(TextMatchRule.Exact, question.MatchRule);
        Assert.Equal(TextMatchRule.Exact, question.Versions[0].MatchRule);
    }

    [Fact]
    public void ALenientTextQuestion_KeepsItsRule_OnTheQuestionAndOnItsFirstVersion()
    {
        var question = Capital(TextMatchRule.Lenient, "Paris");

        Assert.Equal(TextMatchRule.Lenient, question.MatchRule);
        Assert.Equal(TextMatchRule.Lenient, question.Versions[0].MatchRule);
    }

    [Fact]
    public void AMultipleChoiceQuestion_CannotCarryARule()
    {
        var error = Assert.Throws<InvalidQuestionError>(() =>
            Question.Create("Q?", [new("A", true), new("B", false)], Guid.NewGuid(), Now, matchRule: TextMatchRule.Lenient));

        Assert.Contains("text-answer question only", error.Message);
    }

    [Fact]
    public void ARuleThatIsNotOneOfTheTwo_IsRefused_NotStoredWhereNobodyCanReadIt()
    {
        var error = Assert.Throws<InvalidQuestionError>(() => Capital((TextMatchRule)7, "Paris"));

        Assert.Contains("exact or lenient", error.Message);
    }

    [Fact]
    public void UnderTheExactRule_TwoAnswersThatDifferOnlyInAFullStop_AreTwoAnswers()
    {
        var question = Capital(TextMatchRule.Exact, "Paris", "Paris.");

        Assert.Equal(2, question.AcceptedAnswers.Length);
    }

    [Fact]
    public void UnderTheLenientRule_TwoAnswersThatDifferOnlyInAFullStop_AreTheSameAnswerListedTwice()
    {
        // The same comparison the exam runtime marks with: both would match one typed answer, so listing both is refused.
        var error = Assert.Throws<InvalidQuestionError>(() => Capital(TextMatchRule.Lenient, "Paris", "Paris."));

        Assert.Contains("more than once", error.Message);
    }

    [Fact]
    public void ATranslationOfAQuestion_IsNotAffectedByTheRuleOfAnother()
    {
        var lenient = Capital(TextMatchRule.Lenient, "Paris");
        var exact = Capital(TextMatchRule.Exact, "Paris");

        Assert.NotEqual(lenient.MatchRule, exact.MatchRule);
    }

    [Fact]
    public void Revising_WithNoRuleGiven_KeepsTheQuestionsRule_SoAClientThatDoesNotKnowTheRuleCannotResetIt()
    {
        var question = Capital(TextMatchRule.Lenient, "Paris");

        question.Revise("Capital of France?", [], answered: false, nowUtc: Now.AddDays(1), isTextAnswer: true, acceptedAnswers: ["Paris", "Lutetia"]);

        Assert.Equal(TextMatchRule.Lenient, question.MatchRule);
        Assert.Equal(TextMatchRule.Lenient, question.Versions.Last().MatchRule);
    }

    [Fact]
    public void BeforeAnyoneAnswers_TheRuleCanBeChangedByAnEdit_AndTheNewVersionHasIt()
    {
        var question = Capital(TextMatchRule.Exact, "Paris");

        question.Revise("Capital of France?", [], answered: false, nowUtc: Now.AddDays(1), isTextAnswer: true, acceptedAnswers: ["Paris"], matchRule: TextMatchRule.Lenient);

        Assert.Equal(TextMatchRule.Lenient, question.MatchRule);
        Assert.Equal(TextMatchRule.Exact, question.Versions[0].MatchRule);
        Assert.Equal(TextMatchRule.Lenient, question.Versions.Last().MatchRule);
    }

    [Fact]
    public void OnceAnswered_TheRuleCannotChangeByAnEdit_BecauseItDecidesWhoWasRight()
    {
        var question = Capital(TextMatchRule.Exact, "Paris");

        Assert.Throws<QuestionLockedError>(() =>
            question.Revise("Capital of France?", [], answered: true, nowUtc: Now, isTextAnswer: true, acceptedAnswers: ["Paris"], matchRule: TextMatchRule.Lenient));

        Assert.Equal(TextMatchRule.Exact, question.MatchRule);
    }

    [Fact]
    public void OnceAnswered_SavingWithTheSameRuleStillLetsTheWordingChange()
    {
        var question = Capital(TextMatchRule.Lenient, "Paris");

        question.Revise("What is the capital of France?", [], answered: true, nowUtc: Now, isTextAnswer: true, acceptedAnswers: ["Paris"], matchRule: TextMatchRule.Lenient);

        Assert.Equal("What is the capital of France?", question.Text);
        Assert.Equal(TextMatchRule.Lenient, question.MatchRule);
    }

    [Fact]
    public void ATextQuestionTurnedIntoMultipleChoice_LosesItsRule()
    {
        var question = Capital(TextMatchRule.Lenient, "Paris");

        question.Revise("Q?", [new QuestionOptionEdit(null, "A", true, false), new QuestionOptionEdit(null, "B", false, false)], answered: false, nowUtc: Now.AddDays(1));

        Assert.Equal(TextMatchRule.Exact, question.MatchRule);
    }

    [Fact]
    public void ARuleSentForAQuestionThatIsBecomingMultipleChoice_IsRefused()
    {
        var question = Capital(TextMatchRule.Lenient, "Paris");

        Assert.Throws<InvalidQuestionError>(() =>
            question.Revise("Q?", [new QuestionOptionEdit(null, "A", true, false), new QuestionOptionEdit(null, "B", false, false)], answered: false, nowUtc: Now.AddDays(1), matchRule: TextMatchRule.Lenient));
    }

    [Fact]
    public void ACorrection_CanMakeTheRuleLenient_EvenWithTheAnswersUnchanged_AndTakesAVersion()
    {
        var question = Capital(TextMatchRule.Exact, "Paris");
        var later = Now.AddDays(1);

        var changed = question.CorrectAcceptedAnswers(["Paris"], later, TextMatchRule.Lenient);

        Assert.True(changed);
        Assert.Equal(TextMatchRule.Lenient, question.MatchRule);
        Assert.Equal(later, question.AnswerKeyCorrectedAtUtc);
        Assert.Equal(2, question.Versions.Count);
        Assert.Equal(TextMatchRule.Exact, question.Versions[0].MatchRule);
        Assert.Equal(TextMatchRule.Lenient, question.Versions[1].MatchRule);
    }

    [Fact]
    public void ACorrection_WithNeitherADifferentRuleNorDifferentAnswers_ChangesNothing()
    {
        var question = Capital(TextMatchRule.Lenient, "Paris");

        var changed = question.CorrectAcceptedAnswers(["PARIS."], Now.AddDays(1), TextMatchRule.Lenient);

        Assert.False(changed);
        Assert.Single(question.Versions);
    }

    [Fact]
    public void ACorrection_WithNoRuleGiven_KeepsTheRule()
    {
        var question = Capital(TextMatchRule.Lenient, "Paris");

        question.CorrectAcceptedAnswers(["Paris", "Lutetia"], Now.AddDays(1));

        Assert.Equal(TextMatchRule.Lenient, question.MatchRule);
        Assert.Equal(TextMatchRule.Lenient, question.Versions.Last().MatchRule);
    }

    [Fact]
    public void ACorrectionToTheLenientRule_RefusesAnswersThatBecomeTheSame_AndLeavesTheQuestionAsItWas()
    {
        var question = Capital(TextMatchRule.Exact, "Paris", "Paris.");

        Assert.Throws<InvalidQuestionError>(() => question.CorrectAcceptedAnswers(["Paris", "Paris."], Now.AddDays(1), TextMatchRule.Lenient));

        Assert.Equal(TextMatchRule.Exact, question.MatchRule);
        Assert.Single(question.Versions);
    }

    [Fact]
    public void ACorrection_CanDropBackToTheExactRule()
    {
        var question = Capital(TextMatchRule.Lenient, "Paris");

        var changed = question.CorrectAcceptedAnswers(["Paris"], Now.AddDays(1), TextMatchRule.Exact);

        Assert.True(changed);
        Assert.Equal(TextMatchRule.Exact, question.MatchRule);
    }
}
