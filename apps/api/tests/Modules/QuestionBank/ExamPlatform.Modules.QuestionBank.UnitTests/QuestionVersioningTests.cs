using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>
/// A question's version history (FR-7): every accepted change to its gradable content takes a new, never-edited
/// snapshot, while changes that cannot affect what a candidate saw or how they were marked take none.
/// </summary>
public class QuestionVersioningTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    private static Question Capitals() =>
        Question.Create("Capital of France?", [new("Paris", true), new("Rome", false), new("Oslo", false)], Guid.NewGuid(), Now);

    private static List<QuestionOptionEdit> Unchanged(Question question) =>
        question.Options.Select(o => new QuestionOptionEdit(o.Id, o.Text, o.IsCorrect)).ToList();

    [Fact]
    public void Creating_TakesVersion1_WithTheQuestionsTextAndOptions()
    {
        var question = Capitals();

        Assert.Equal(1, question.CurrentVersionNumber);
        var version = Assert.Single(question.Versions);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("Capital of France?", version.Text);
        Assert.Equal(Now, version.CreatedAtUtc);
        Assert.Equal(
            question.Options.Select(o => (o.Id, o.Text, o.IsCorrect, o.Order, o.IsPinned)),
            version.Options.Select(o => (o.OptionId, o.Text, o.IsCorrect, o.Order, o.IsPinned)));
    }

    [Fact]
    public void RevisingSuccessfully_TakesANewVersion_AndKeepsTheOldOneUnchanged()
    {
        var question = Capitals();
        var firstVersion = question.Versions[0];

        question.Revise("Capital of Spain?", Unchanged(question), answered: false, nowUtc: Now.AddDays(1));

        Assert.Equal(2, question.CurrentVersionNumber);
        Assert.Equal(2, question.Versions.Count);
        Assert.Same(firstVersion, question.Versions[0]);
        Assert.Equal("Capital of France?", question.Versions[0].Text);
        Assert.Equal("Capital of Spain?", question.Versions[1].Text);
        Assert.Equal(Now.AddDays(1), question.Versions[1].CreatedAtUtc);
    }

    [Fact]
    public void ARefusedRevision_TakesNoVersion()
    {
        var question = Capitals();
        var edits = Unchanged(question);
        edits[0] = edits[0] with { Text = "" };

        Assert.ThrowsAny<Exception>(() => question.Revise("Q?", edits, answered: false, nowUtc: Now.AddDays(1)));

        Assert.Equal(1, question.Versions.Count);
    }

    [Fact]
    public void CorrectingTheAnswerKey_TakesANewVersion_RecordingTheNewKey()
    {
        var question = Capitals();
        var rome = question.Options[1].Id;

        question.CorrectAnswerKey([rome], Now.AddDays(1));

        Assert.Equal(2, question.CurrentVersionNumber);
        var latest = question.Versions[1];
        Assert.Equal(rome, latest.Options.Single(o => o.IsCorrect).OptionId);
        Assert.Equal(Now.AddDays(1), latest.CreatedAtUtc);
    }

    [Fact]
    public void CorrectingTheAnswerKeyToTheSameOptions_TakesNoVersion_BecauseNothingChanged()
    {
        var question = Capitals();
        var paris = question.Options[0].Id;

        question.CorrectAnswerKey([paris], Now.AddDays(1));

        Assert.Equal(1, question.Versions.Count);
    }

    [Fact]
    public void ClassifyingOrFiling_TakesNoVersion_BecauseNeitherReachesACandidateOrAffectsMarking()
    {
        var question = Capitals();

        question.Classify(QuestionDifficulty.Hard, ["geography"]);
        question.FileUnder(Guid.NewGuid());

        Assert.Equal(1, question.Versions.Count);
    }

    [Fact]
    public void OnceAnswered_CorrectingOnlyTheWording_StillTakesANewVersion()
    {
        var question = Capitals();

        question.Revise("Capital of France ?", Unchanged(question).Select(o => o with { Text = o.Text + "." }).ToList(), answered: true, nowUtc: Now.AddDays(1));

        Assert.Equal(2, question.CurrentVersionNumber);
        Assert.Equal(["Paris.", "Rome.", "Oslo."], question.Versions[1].Options.OrderBy(o => o.Order).Select(o => o.Text));
    }
}
