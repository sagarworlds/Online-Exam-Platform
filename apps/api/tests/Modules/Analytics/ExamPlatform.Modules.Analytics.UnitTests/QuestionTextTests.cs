using ExamPlatform.Modules.Analytics.Application;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>The short plain-text preview of a question shown in the item analysis table.</summary>
public class QuestionTextTests
{
    [Fact]
    public void InlineMarkup_IsRemovedWithoutSplittingTheWord()
    {
        Assert.Equal("What is force?", QuestionText.Preview("<p>What is <em>force</em>?</p>"));
    }

    [Fact]
    public void BlockElements_SeparateTheWordsEitherSide()
    {
        Assert.Equal("First line Second line", QuestionText.Preview("<p>First line</p><p>Second line</p>"));
    }

    [Fact]
    public void Entities_AreDecoded_AndWhitespaceCollapsed()
    {
        Assert.Equal("a < b & c", QuestionText.Preview("a&nbsp;&lt;  b &amp;\n\tc"));
    }

    [Fact]
    public void ALongQuestion_IsCutToThePreviewLength_WithAnEllipsis()
    {
        var longText = string.Join(' ', Enumerable.Repeat("word", 80));

        var preview = QuestionText.Preview(longText);

        Assert.True(preview.Length <= QuestionText.PreviewLength);
        Assert.EndsWith("…", preview);
    }

    [Fact]
    public void AQuestionWithNoText_HasAnEmptyPreview()
    {
        Assert.Equal(string.Empty, QuestionText.Preview("<p> </p>"));
    }
}
