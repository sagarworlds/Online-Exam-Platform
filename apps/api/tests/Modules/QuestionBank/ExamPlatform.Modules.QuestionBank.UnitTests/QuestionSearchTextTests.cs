using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>The readable copy of a question's text that searching reads.</summary>
public class QuestionSearchTextTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);

    private static Question Capitals() =>
        Question.Create("<p>Capital of France?</p>", [new("Paris", true), new("Rome", false)], Guid.NewGuid(), Now);

    [Fact]
    public void ANewQuestion_HasNoSearchText_UntilItIsIndexed() => Assert.Equal(string.Empty, Capitals().SearchText);

    [Fact]
    public void IndexingKeepsTheTrimmedText()
    {
        var question = Capitals();

        question.IndexText("  Capital of France?  ");

        Assert.Equal("Capital of France?", question.SearchText);
    }

    [Fact]
    public void IndexingNull_LeavesItEmptyRatherThanNull()
    {
        var question = Capitals();
        question.IndexText("something");

        question.IndexText(null);

        Assert.Equal(string.Empty, question.SearchText);
    }
}
