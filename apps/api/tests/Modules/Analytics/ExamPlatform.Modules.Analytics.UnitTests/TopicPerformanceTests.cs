using ExamPlatform.Modules.Analytics.Domain;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>How a candidate's answers group under the topic labels of their questions (FR-36, accuracy by topic).</summary>
public class TopicPerformanceTests
{
    private static AnswerTally Correct() => AnswerTally.Of(1, 0, 0, 0);

    private static AnswerTally Wrong() => AnswerTally.Of(0, 1, 0, 0);

    private static AnswerTally Unanswered() => AnswerTally.Of(0, 0, 0, 1);

    [Fact]
    public void TopicsAreListedWeakestAccuracyFirst_WithAnUnansweredTopicLast()
    {
        var topics = TopicPerformance.Of(
        [
            (["strong"], Correct()),
            (["weak"], Wrong()),
            (["blank"], Unanswered()),
        ]);

        // A topic nobody answered says nothing about where the candidate is weak, so it sorts last rather than as the weakest.
        Assert.Equal(["weak", "strong", "blank"], topics.Select(t => t.Name));
        Assert.Null(topics[2].Answers.Accuracy);
    }

    [Fact]
    public void LabelsMatchIgnoringCaseAndSurroundingSpaces()
    {
        var topics = TopicPerformance.Of(
        [
            (["Algebra"], Correct()),
            ([" algebra "], Wrong()),
        ]);

        var algebra = Assert.Single(topics);
        Assert.Equal(2, algebra.QuestionCount);
        Assert.Equal(50m, algebra.Answers.Accuracy);
    }

    [Fact]
    public void AQuestionWithNoLabel_IsLeftOut()
    {
        var topics = TopicPerformance.Of(
        [
            ([], Wrong()),
            ([""], Wrong()),
            (["   "], Correct()),
        ]);

        Assert.Empty(topics);
    }

    [Fact]
    public void AQuestionWithTwoLabels_CountsUnderEach()
    {
        var topics = TopicPerformance.Of([(["algebra", "graphs"], Correct())]);

        Assert.Equal(["algebra", "graphs"], topics.Select(t => t.Name));
        Assert.All(topics, t => Assert.Equal(1, t.QuestionCount));
    }
}
