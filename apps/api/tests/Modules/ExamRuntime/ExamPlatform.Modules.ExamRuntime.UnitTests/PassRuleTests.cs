using ExamPlatform.Modules.ExamRuntime.Application;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>The pass rule (FR-34): a score passes when it reaches the pass mark's share of the total marks, and nothing is judged without a mark.</summary>
public class PassRuleTests
{
    [Fact]
    public void AnExamWithNoPassMark_JudgesNothing()
    {
        Assert.Null(PassRule.Of(score: 20m, maxScore: 20m, passingPercentage: null));
    }

    [Fact]
    public void AnExamWithNoMarks_JudgesNothing_BecauseThereIsNoShareToTake()
    {
        Assert.Null(PassRule.Of(score: 0m, maxScore: 0m, passingPercentage: 40));
    }

    [Fact]
    public void AScoreExactlyAtTheMark_Passes()
    {
        // 40% of 20 marks is 8 marks, so 8 passes and anything under it does not.
        Assert.True(PassRule.Of(score: 8m, maxScore: 20m, passingPercentage: 40));
    }

    [Fact]
    public void AScoreJustUnderTheMark_Fails()
    {
        Assert.False(PassRule.Of(score: 7.9m, maxScore: 20m, passingPercentage: 40));
    }

    [Fact]
    public void ANegativeScore_FailsAnyPassMark()
    {
        // Negative marking can take a score below zero, which is never a pass.
        Assert.False(PassRule.Of(score: -2m, maxScore: 20m, passingPercentage: 1));
    }

    [Fact]
    public void AFullScore_PassesEvenTheHighestMark()
    {
        Assert.True(PassRule.Of(score: 20m, maxScore: 20m, passingPercentage: 100));
    }
}
