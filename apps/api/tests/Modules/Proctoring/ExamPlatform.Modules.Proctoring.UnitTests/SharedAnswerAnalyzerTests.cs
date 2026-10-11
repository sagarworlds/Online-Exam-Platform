using ExamPlatform.Modules.Proctoring.Domain;

namespace ExamPlatform.Modules.Proctoring.UnitTests;

/// <summary>The shared-answer signal: rare identical wrong answers, counted against one partner at a time (FR-27).</summary>
public class SharedAnswerAnalyzerTests
{
    private static readonly Guid QuestionA = Guid.NewGuid();
    private static readonly Guid QuestionB = Guid.NewGuid();
    private static readonly Guid QuestionC = Guid.NewGuid();

    private static WrongAnswerKey Wrong(Guid question, string choice = "option-b") => new(question, choice);

    [Fact]
    public void TwoCandidatesWithTheSameRareWrongAnswer_ShareIt()
    {
        var first = RiskTestData.Attempt(wrongAnswers: [Wrong(QuestionA)]);
        var second = RiskTestData.Attempt(wrongAnswers: [Wrong(QuestionA)]);

        var counts = SharedAnswerAnalyzer.MaxSharedWithOneAttempt([first, second], maxSharers: 2);

        Assert.Equal(1, counts[first.AttemptId]);
        Assert.Equal(1, counts[second.AttemptId]);
    }

    [Fact]
    public void ACandidateAloneWithAWrongAnswer_SharesNothing()
    {
        var alone = RiskTestData.Attempt(wrongAnswers: [Wrong(QuestionA)]);
        var other = RiskTestData.Attempt(wrongAnswers: [Wrong(QuestionB)]);

        var counts = SharedAnswerAnalyzer.MaxSharedWithOneAttempt([alone, other], maxSharers: 2);

        Assert.Equal(0, counts[alone.AttemptId]);
    }

    [Fact]
    public void AWrongAnswerThatManyCandidatesMake_IsACommonMistake_AndNotCounted()
    {
        // Three candidates pick the same wrong option. With at most two sharers allowed, that is a distractor, not copying.
        var attempts = Enumerable.Range(0, 3).Select(_ => RiskTestData.Attempt(wrongAnswers: [Wrong(QuestionA)])).ToList();

        var counts = SharedAnswerAnalyzer.MaxSharedWithOneAttempt(attempts, maxSharers: 2);

        Assert.All(attempts, a => Assert.Equal(0, counts[a.AttemptId]));
    }

    [Fact]
    public void TheCount_IsTheBestSinglePartner_NotTheSumOverAllPartners()
    {
        // A shares two wrong answers with B, and one with C. The answer is the most with any one candidate: two, not three.
        var a = RiskTestData.Attempt(wrongAnswers: [Wrong(QuestionA), Wrong(QuestionB), Wrong(QuestionC, "other")]);
        var b = RiskTestData.Attempt(wrongAnswers: [Wrong(QuestionA), Wrong(QuestionB)]);
        var c = RiskTestData.Attempt(wrongAnswers: [Wrong(QuestionC, "other")]);

        var counts = SharedAnswerAnalyzer.MaxSharedWithOneAttempt([a, b, c], maxSharers: 2);

        Assert.Equal(2, counts[a.AttemptId]);
        Assert.Equal(2, counts[b.AttemptId]);
        Assert.Equal(1, counts[c.AttemptId]);
    }

    [Fact]
    public void TheSameQuestionWithADifferentChoice_IsNotShared()
    {
        var first = RiskTestData.Attempt(wrongAnswers: [Wrong(QuestionA, "option-b")]);
        var second = RiskTestData.Attempt(wrongAnswers: [Wrong(QuestionA, "option-c")]);

        var counts = SharedAnswerAnalyzer.MaxSharedWithOneAttempt([first, second], maxSharers: 2);

        Assert.Equal(0, counts[first.AttemptId]);
        Assert.Equal(0, counts[second.AttemptId]);
    }

    [Fact]
    public void AnAttemptWithNoWrongAnswers_ScoresZero()
    {
        var attempt = RiskTestData.Attempt();

        var counts = SharedAnswerAnalyzer.MaxSharedWithOneAttempt([attempt], maxSharers: 2);

        Assert.Equal(0, counts[attempt.AttemptId]);
    }

    [Fact]
    public void WithALargerSharerLimit_ACommonerChoiceCounts()
    {
        var attempts = Enumerable.Range(0, 3).Select(_ => RiskTestData.Attempt(wrongAnswers: [Wrong(QuestionA)])).ToList();

        var counts = SharedAnswerAnalyzer.MaxSharedWithOneAttempt(attempts, maxSharers: 3);

        Assert.All(attempts, a => Assert.Equal(1, counts[a.AttemptId]));
    }
}
