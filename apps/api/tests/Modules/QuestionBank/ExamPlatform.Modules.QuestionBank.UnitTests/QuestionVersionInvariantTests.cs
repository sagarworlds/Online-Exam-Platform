using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>
/// The invariant the version cache rests on: whatever changes a question's content takes a new <see cref="QuestionVersion"/> holding exactly that
/// content, and nothing else changes it. So the version in force always equals the question, and what is kept for a question and a version number
/// can never go out of date. If one of these fails, a change was added that alters content without a version, and the cache must not be used until
/// it is fixed.
/// </summary>
public class QuestionVersionInvariantTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);

    private static void AssertNewestVersionIsTheQuestion(Question question)
    {
        var newest = question.Versions.OrderBy(v => v.VersionNumber).Last();
        Assert.Equal(question.Versions.Count, newest.VersionNumber);
        Assert.Equal(Enumerable.Range(1, question.Versions.Count), question.Versions.Select(v => v.VersionNumber).Order());
        Assert.Equal(question.Text, newest.Text);
        Assert.Equal(question.AllowsMultiple, newest.AllowsMultiple);
        Assert.Equal(question.IsTextAnswer, newest.IsTextAnswer);
        Assert.Equal(question.AcceptedAnswers, newest.AcceptedAnswers);
        Assert.Equal(question.MatchRule, newest.MatchRule);
        Assert.Equal(question.Explanation, newest.Explanation);
        Assert.Equal(
            question.Options.OrderBy(o => o.Order).Select(o => (o.Id, o.Text, o.IsCorrect, o.IsPinned, o.Order)),
            newest.Options.OrderBy(o => o.Order).Select(o => (Id: o.OptionId, o.Text, o.IsCorrect, o.IsPinned, o.Order)));
    }

    private static List<QuestionOptionEdit> Edits(Question question, Func<QuestionOption, QuestionOptionEdit>? change = null) =>
        question.Options.OrderBy(o => o.Order).Select(o => change?.Invoke(o) ?? new QuestionOptionEdit(o.Id, o.Text, o.IsCorrect, o.IsPinned)).ToList();

    private static Question Choice() =>
        Question.Create("<p>Which?</p>", [new NewQuestionOption("A", true), new NewQuestionOption("B", false), new NewQuestionOption("C", false, IsPinned: true)],
            Guid.NewGuid(), Now, explanation: "Because A.");

    [Fact]
    public void ANewQuestion_HasItsFirstVersion()
    {
        var question = Choice();

        Assert.Single(question.Versions);
        AssertNewestVersionIsTheQuestion(question);
    }

    [Fact]
    public void ARevision_TakesAVersionWithTheNewContent_OptionsTextAndExplanationIncluded()
    {
        var question = Choice();
        var edits = Edits(question, o => new QuestionOptionEdit(o.Id, o.Text + " (reworded)", o.IsCorrect, o.IsPinned));
        edits.Add(new QuestionOptionEdit(null, "D", false));

        question.Revise("<p>Which one?</p>", edits, answered: false, allowsMultiple: false, Now.AddMinutes(1), explanation: "A, as before.");

        Assert.Equal(2, question.Versions.Count);
        AssertNewestVersionIsTheQuestion(question);
    }

    [Fact]
    public void ARevisionThatChangesNothing_StillTakesAVersion_SoTheNumberAndTheContentStayInStep()
    {
        var question = Choice();

        question.Revise(question.Text, Edits(question), answered: false, allowsMultiple: false, Now.AddMinutes(1), explanation: question.Explanation);

        Assert.Equal(2, question.Versions.Count);
        AssertNewestVersionIsTheQuestion(question);
    }

    [Fact]
    public void AWordingFix_ToAnAnsweredQuestion_TakesAVersion()
    {
        var question = Choice();

        question.Revise("<p>Which? (corrected)</p>", Edits(question, o => new QuestionOptionEdit(o.Id, o.Text.ToLowerInvariant(), o.IsCorrect, o.IsPinned)), answered: true, allowsMultiple: false, Now.AddMinutes(1), explanation: question.Explanation);

        Assert.Equal(2, question.Versions.Count);
        AssertNewestVersionIsTheQuestion(question);
    }

    [Fact]
    public void AnAnswerKeyCorrection_TakesAVersionWithTheCorrectedKey_AndNoneWhenTheKeyIsUnchanged()
    {
        var question = Choice();
        var b = question.Options.Single(o => o.Text == "B").Id;

        Assert.True(question.CorrectAnswerKey([b], Now.AddMinutes(1)));
        AssertNewestVersionIsTheQuestion(question);
        Assert.Equal(2, question.Versions.Count);

        Assert.False(question.CorrectAnswerKey([b], Now.AddMinutes(2)));
        Assert.Equal(2, question.Versions.Count);
        AssertNewestVersionIsTheQuestion(question);
    }

    [Fact]
    public void AMultipleAnswerQuestion_KeepsItsShapeInEveryVersion()
    {
        var question = Question.Create(
            "<p>Which are prime?</p>", [new NewQuestionOption("2", true), new NewQuestionOption("3", true), new NewQuestionOption("4", false)],
            Guid.NewGuid(), Now, allowsMultiple: true);
        AssertNewestVersionIsTheQuestion(question);

        var four = question.Options.Single(o => o.Text == "4").Id;
        var two = question.Options.Single(o => o.Text == "2").Id;
        question.CorrectAnswerKey([two, four], Now.AddMinutes(1));

        Assert.True(question.AllowsMultiple);
        AssertNewestVersionIsTheQuestion(question);
    }

    [Fact]
    public void ATextQuestion_TakesAVersionWithItsAcceptedAnswersAndRule_OnCreateReviseAndCorrection()
    {
        var question = Question.Create(
            "<p>Capital of France?</p>", [], Guid.NewGuid(), Now, isTextAnswer: true, acceptedAnswers: ["Paris"], matchRule: TextMatchRule.Exact);
        AssertNewestVersionIsTheQuestion(question);

        question.Revise("<p>Capital of France?!</p>", [], answered: true, allowsMultiple: false, Now.AddMinutes(1), isTextAnswer: true, acceptedAnswers: ["Paris"], explanation: "Seine.", matchRule: TextMatchRule.Exact);
        AssertNewestVersionIsTheQuestion(question);

        Assert.True(question.CorrectAcceptedAnswers(["Paris", "paris, france"], Now.AddMinutes(2), TextMatchRule.Lenient));
        AssertNewestVersionIsTheQuestion(question);
        Assert.Equal(3, question.Versions.Count);

        Assert.False(question.CorrectAcceptedAnswers(["Paris", "paris, france"], Now.AddMinutes(3), TextMatchRule.Lenient));
        Assert.Equal(3, question.Versions.Count);
    }

    [Fact]
    public void LabellingFilingAndReview_ChangeNoContent_AndTakeNoVersion()
    {
        var question = Choice();
        var versions = question.Versions.Count;

        question.Classify(QuestionDifficulty.Hard, ["algebra"]);
        question.FileUnder(Guid.NewGuid());
        question.Retire(Guid.NewGuid(), "Reviewer", "Out of date", Now.AddMinutes(1));
        question.Restore(Guid.NewGuid(), "Reviewer", "Back in", Now.AddMinutes(2));

        Assert.Equal(versions, question.Versions.Count);
        AssertNewestVersionIsTheQuestion(question);
    }

    [Fact]
    public void ASeriesOfChanges_NeverReusesOrSkipsAVersionNumber()
    {
        var question = Choice();
        for (var round = 1; round <= 4; round++)
        {
            question.Revise($"<p>Wording {round}</p>", Edits(question), answered: false, allowsMultiple: false, Now.AddMinutes(round), explanation: question.Explanation);
            question.CorrectAnswerKey([question.Options.OrderBy(o => o.Order).ElementAt(round % 3).Id], Now.AddMinutes(round).AddSeconds(30));
            AssertNewestVersionIsTheQuestion(question);
        }

        Assert.Equal(Enumerable.Range(1, question.Versions.Count), question.Versions.Select(v => v.VersionNumber).Order());
    }
}
