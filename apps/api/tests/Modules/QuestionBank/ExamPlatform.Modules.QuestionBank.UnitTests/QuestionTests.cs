using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

public class QuestionTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private static List<NewQuestionOption> Options(params (string? Text, bool Correct)[] options) =>
        options.Select(o => new NewQuestionOption(o.Text, o.Correct)).ToList();

    private static List<NewQuestionOption> TwoOptions() => Options(("Paris", true), ("Rome", false));

    [Fact]
    public void Create_WithValidInput_StoresTrimmedTextAndOptionsInOrder()
    {
        var question = Question.Create("  Capital of France?  ", Options((" Paris ", true), ("Rome", false), ("Oslo", false)), Author, Now);

        Assert.Equal("Capital of France?", question.Text);
        Assert.Equal(Author, question.CreatedBy);
        Assert.Equal(Now, question.CreatedAtUtc);
        Assert.Equal(["Paris", "Rome", "Oslo"], question.Options.Select(o => o.Text));
        Assert.Equal([1, 2, 3], question.Options.Select(o => o.Order));
        Assert.Equal([true, false, false], question.Options.Select(o => o.IsCorrect));
        Assert.All(question.Options, o => Assert.Equal(question.Id, o.QuestionId));
        Assert.NotEqual(Guid.Empty, question.Id);
    }

    [Fact]
    public void Create_IsUnfiledByDefault_AndRemembersTheChapterItIsFiledUnder()
    {
        Assert.Null(Question.Create("Q?", TwoOptions(), Author, Now).ChapterId);

        var chapter = Guid.NewGuid();
        Assert.Equal(chapter, Question.Create("Q?", TwoOptions(), Author, Now, chapter).ChapterId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankText_Throws(string? text)
    {
        var error = Assert.Throws<InvalidQuestionError>(() => Question.Create(text, TwoOptions(), Author, Now));
        Assert.Equal("invalid_question", error.ErrorCode);
        Assert.Equal(400, error.HttpStatusCode);
    }

    [Fact]
    public void Create_WithTextLargerThanTheHtmlCeiling_Throws() =>
        Assert.Throws<InvalidQuestionError>(() =>
            Question.Create(new string('x', Question.MaxHtmlLength + 1), TwoOptions(), Author, Now));

    [Fact]
    public void Create_WithoutOptions_Throws() =>
        Assert.Throws<InvalidQuestionError>(() => Question.Create("Q?", null, Author, Now));

    [Fact]
    public void Create_WithOneOption_Throws() =>
        Assert.Throws<InvalidQuestionError>(() => Question.Create("Q?", Options(("Only", true)), Author, Now));

    [Fact]
    public void Create_WithMoreThanSixOptions_Throws() =>
        Assert.Throws<InvalidQuestionError>(() => Question.Create(
            "Q?",
            Options(("1", true), ("2", false), ("3", false), ("4", false), ("5", false), ("6", false), ("7", false)),
            Author,
            Now));

    [Fact]
    public void Create_WithNoCorrectOption_Throws() =>
        Assert.Throws<InvalidQuestionError>(() => Question.Create("Q?", Options(("A", false), ("B", false)), Author, Now));

    [Fact]
    public void Create_WithTwoCorrectOptions_Throws() =>
        Assert.Throws<InvalidQuestionError>(() => Question.Create("Q?", Options(("A", true), ("B", true)), Author, Now));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Create_WithBlankOptionText_Throws(string? optionText) =>
        Assert.Throws<InvalidQuestionError>(() => Question.Create("Q?", Options(("A", true), (optionText, false)), Author, Now));

    [Fact]
    public void Create_WithTooLongOptionText_Throws() =>
        Assert.Throws<InvalidQuestionError>(() => Question.Create(
            "Q?", Options(("A", true), (new string('x', Question.MaxOptionTextLength + 1), false)), Author, Now));
}
