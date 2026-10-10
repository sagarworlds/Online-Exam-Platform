using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>
/// A question's explanation (FR-33): plain text the author writes for the answer review. It is versioned with the question, so a candidate
/// reads the explanation of the version they sat, and it is content, so editing it sends an approved question back for approval.
/// </summary>
public class QuestionExplanationTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Staff = Guid.NewGuid();

    private static Question Capitals(string? explanation = null) =>
        Question.Create("Capital of France?", [new("Paris", true), new("Rome", false)], Staff, Now, explanation: explanation);

    private static List<QuestionOptionEdit> Same(Question question) =>
        question.Options.OrderBy(o => o.Order).Select(o => new QuestionOptionEdit(o.Id, o.Text, o.IsCorrect)).ToList();

    [Fact]
    public void AQuestionCreatedWithAnExplanation_KeepsItInItsFirstVersion()
    {
        var question = Capitals("Paris has been the capital for centuries.");

        Assert.Equal("Paris has been the capital for centuries.", question.Explanation);
        Assert.Equal("Paris has been the capital for centuries.", Assert.Single(question.Versions).Explanation);
    }

    [Fact]
    public void ABlankExplanation_IsNoExplanation()
    {
        Assert.Null(Capitals("   ").Explanation);
        Assert.Null(Capitals(string.Empty).Explanation);
    }

    [Fact]
    public void AnExplanation_IsTrimmedBeforeItIsStored()
    {
        Assert.Equal("Because.", Capitals("  Because.\n").Explanation);
    }

    [Fact]
    public void AnExplanationLongerThanTheLimit_IsRefused_WhenCreating()
    {
        var tooLong = new string('x', Question.MaxExplanationLength + 1);

        Assert.Throws<InvalidQuestionError>(() => Capitals(tooLong));
    }

    [Fact]
    public void ARevisedExplanation_TakesANewVersion_AndTheOldVersionKeepsItsOwn()
    {
        var question = Capitals("Old reasoning.");
        var first = question.Versions[0];

        question.Revise("Capital of France?", Same(question), answered: false, nowUtc: Now.AddDays(1), explanation: "New reasoning.");

        Assert.Equal(2, question.CurrentVersionNumber);
        Assert.Same(first, question.Versions[0]);
        Assert.Equal("Old reasoning.", question.Versions[0].Explanation);
        Assert.Equal("New reasoning.", question.Versions[1].Explanation);
    }

    [Fact]
    public void ARevisionWithoutAnExplanation_ClearsIt_BecauseTheEditIsTheWholeNewContent()
    {
        var question = Capitals("Old reasoning.");

        question.Revise("Capital of France?", Same(question), answered: false, nowUtc: Now.AddDays(1));

        Assert.Null(question.Explanation);
    }

    [Fact]
    public void AnExplanationTooLong_RefusesTheWholeRevision_AndTakesNoVersion()
    {
        var question = Capitals("Kept.");
        var tooLong = new string('x', Question.MaxExplanationLength + 1);

        Assert.Throws<InvalidQuestionError>(() =>
            question.Revise("Changed?", Same(question), answered: false, nowUtc: Now.AddDays(1), explanation: tooLong));

        Assert.Equal("Capital of France?", question.Text);
        Assert.Equal("Kept.", question.Explanation);
        Assert.Equal(1, question.Versions.Count);
    }

    [Fact]
    public void EditingTheExplanationOfAnApprovedQuestion_SendsItBackForApproval()
    {
        var question = Capitals("Old.");
        question.SubmitForReview(Staff, null, null, Now);
        question.Approve(Guid.NewGuid(), null, null, Now);
        Assert.Equal(QuestionStatus.Approved, question.Status);

        question.Revise("Capital of France?", Same(question), answered: false, nowUtc: Now.AddDays(1), explanation: "New.");

        Assert.Equal(QuestionStatus.Draft, question.Status);
    }

    [Fact]
    public void AnAnsweredQuestion_MayHaveItsExplanationCorrected_BecauseItIsWordingAndCannotChangeAnyMark()
    {
        var question = Capitals();

        question.Revise("Capital of France?", Same(question), answered: true, nowUtc: Now.AddDays(1), explanation: "Clarified.");

        Assert.Equal("Clarified.", question.Explanation);
    }
}
