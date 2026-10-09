using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>Difficulty and topic labels on a question: how they are cleaned, what they may not be, and that they never touch marking.</summary>
public class QuestionClassifyTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);

    private static Question Capitals() =>
        Question.Create("Capital of France?", [new("Paris", true), new("Rome", false)], Guid.NewGuid(), Now);

    [Fact]
    public void ANewQuestion_HasNoDifficultyAndNoTopics()
    {
        var question = Capitals();

        Assert.Null(question.Difficulty);
        Assert.Empty(question.Topics);
    }

    [Fact]
    public void CreateKeepsTheLabelsItWasGiven()
    {
        var question = Question.Create(
            "Q?", [new("A", true), new("B", false)], Guid.NewGuid(), Now, difficulty: QuestionDifficulty.Hard, topics: ["Geography"]);

        Assert.Equal(QuestionDifficulty.Hard, question.Difficulty);
        Assert.Equal(["geography"], question.Topics);
    }

    [Fact]
    public void Topics_AreTrimmedLowerCasedAndDeDuplicated_AndBlankOnesAreDropped()
    {
        var question = Capitals();

        question.Classify(null, ["  Fractions ", "FRACTIONS", "Long   Division", "", "   ", null]);

        Assert.Equal(["fractions", "long division"], question.Topics);
    }

    [Fact]
    public void Classifying_ReplacesWhatWasThere_AndNullClearsIt()
    {
        var question = Capitals();
        question.Classify(QuestionDifficulty.Easy, ["a"]);

        question.Classify(QuestionDifficulty.Medium, ["b", "c"]);
        Assert.Equal(QuestionDifficulty.Medium, question.Difficulty);
        Assert.Equal(["b", "c"], question.Topics);

        question.Classify(null, null);
        Assert.Null(question.Difficulty);
        Assert.Empty(question.Topics);
    }

    [Fact]
    public void MoreThanFiveTopics_AreRefused_AndTheQuestionKeepsItsOldLabels()
    {
        var question = Capitals();
        question.Classify(QuestionDifficulty.Easy, ["keep"]);

        var error = Assert.Throws<InvalidQuestionError>(() => question.Classify(QuestionDifficulty.Hard, ["a", "b", "c", "d", "e", "f"]));

        Assert.Contains("at most 5", error.Message);
        Assert.Equal(QuestionDifficulty.Easy, question.Difficulty);
        Assert.Equal(["keep"], question.Topics);
    }

    [Fact]
    public void ATopicThatIsTooLong_IsRefused()
    {
        Assert.Throws<InvalidQuestionError>(() => Capitals().Classify(null, [new string('x', Question.MaxTopicLength + 1)]));
        Capitals().Classify(null, [new string('x', Question.MaxTopicLength)]);
    }

    [Fact]
    public void AnUndefinedDifficulty_IsRefused() =>
        Assert.Throws<InvalidQuestionError>(() => Capitals().Classify((QuestionDifficulty)7, null));

    [Fact]
    public void RevisingAnAnsweredQuestion_LeavesTheLabelsAlone_AndTheyCanStillChange()
    {
        var question = Capitals();
        question.Classify(QuestionDifficulty.Easy, ["capitals"]);
        var unchanged = question.Options.Select(o => new QuestionOptionEdit(o.Id, o.Text, o.IsCorrect)).ToList();

        question.Revise("Capital of France, please?", unchanged, answered: true, nowUtc: Now);
        Assert.Equal(QuestionDifficulty.Easy, question.Difficulty);

        question.Classify(QuestionDifficulty.Hard, ["capitals", "europe"]);
        Assert.Equal(QuestionDifficulty.Hard, question.Difficulty);
    }

    [Theory]
    [InlineData("easy", QuestionDifficulty.Easy)]
    [InlineData("Medium", QuestionDifficulty.Medium)]
    [InlineData(" HARD ", QuestionDifficulty.Hard)]
    public void DifficultyText_IsReadIgnoringCase(string text, QuestionDifficulty expected) =>
        Assert.Equal(expected, QuestionDifficultyText.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NoDifficultyText_MeansNone(string? text) => Assert.Null(QuestionDifficultyText.Parse(text));

    [Theory]
    [InlineData("tricky")]
    [InlineData("7")]
    public void DifficultyTextThatIsNotALevel_IsRefused(string text) =>
        Assert.Throws<InvalidQuestionError>(() => QuestionDifficultyText.Parse(text));

    [Fact]
    public void DifficultyIsWrittenInLowerCase() => Assert.Equal("hard", QuestionDifficultyText.Format(QuestionDifficulty.Hard));
}
