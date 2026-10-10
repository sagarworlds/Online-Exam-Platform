using ExamPlatform.Modules.Proctoring.Domain;
using ExamPlatform.Modules.Proctoring.Domain.Exceptions;

namespace ExamPlatform.Modules.Proctoring.UnitTests;

/// <summary>The configured policy is checked when built, so a bad weight or threshold cannot quietly flag nobody or everybody.</summary>
public class RiskPolicyTests
{
    [Fact]
    public void TheDefaultRules_BuildAPolicy_WithTheirTotalAsTheMaximum()
    {
        var policy = RiskPolicy.Create(RiskTestData.DefaultRules(), 30, 10, 2);

        Assert.Equal(100, policy.MaxScore);
        Assert.Equal(30, policy.FlagThreshold);
        Assert.Equal(10, policy.MinAnswersForPace);
        Assert.Equal(2, policy.MaxSharersPerAnswer);
    }

    [Fact]
    public void ASignalWithoutARule_IsRefused()
    {
        var rules = RiskTestData.DefaultRules().Where(r => r.Kind != RiskSignalKind.Invalidated).ToList();

        var error = Assert.Throws<InvalidRiskPolicyError>(() => RiskPolicy.Create(rules, 30, 10, 2));

        Assert.Contains("Invalidated", error.Message);
    }

    [Fact]
    public void ASignalConfiguredTwice_IsRefused()
    {
        var rules = RiskTestData.DefaultRules().Append(new RiskRule(RiskSignalKind.FocusDepartures, 1, RaisedWhen.AtLeast, 1)).ToList();

        Assert.Throws<InvalidRiskPolicyError>(() => RiskPolicy.Create(rules, 30, 10, 2));
    }

    [Fact]
    public void ANegativeWeight_IsRefused()
    {
        var rules = RiskTestData.DefaultRules().Select(r => r.Kind == RiskSignalKind.ClientChanges ? r with { Weight = -1 } : r).ToList();

        Assert.Throws<InvalidRiskPolicyError>(() => RiskPolicy.Create(rules, 30, 10, 2));
    }

    [Fact]
    public void ANegativeThreshold_IsRefused()
    {
        var rules = RiskTestData.DefaultRules().Select(r => r.Kind == RiskSignalKind.FocusDepartures ? r with { Threshold = -3 } : r).ToList();

        Assert.Throws<InvalidRiskPolicyError>(() => RiskPolicy.Create(rules, 30, 10, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public void AFlagThresholdThatNothingOrEverythingReaches_IsRefused(int threshold) =>
        Assert.Throws<InvalidRiskPolicyError>(() => RiskPolicy.Create(RiskTestData.DefaultRules(), threshold, 10, 2));

    [Fact]
    public void AFlagThresholdEqualToTheTotal_IsAccepted()
    {
        var policy = RiskPolicy.Create(RiskTestData.DefaultRules(), 100, 10, 2);

        Assert.Equal(100, policy.FlagThreshold);
    }

    [Fact]
    public void APaceMinimumBelowOne_IsRefused() =>
        Assert.Throws<InvalidRiskPolicyError>(() => RiskPolicy.Create(RiskTestData.DefaultRules(), 30, 0, 2));

    [Fact]
    public void ASharerLimitBelowTwo_IsRefused() =>
        Assert.Throws<InvalidRiskPolicyError>(() => RiskPolicy.Create(RiskTestData.DefaultRules(), 30, 10, 1));

    [Fact]
    public void RuleFor_ReturnsTheRuleOfThatSignal()
    {
        var policy = RiskPolicy.Create(RiskTestData.DefaultRules(), 30, 10, 2);

        Assert.Equal(RaisedWhen.AtMost, policy.RuleFor(RiskSignalKind.FastCompletion).RaisedWhen);
        Assert.Equal(35, policy.RuleFor(RiskSignalKind.SharedWrongAnswers).Weight);
    }
}
