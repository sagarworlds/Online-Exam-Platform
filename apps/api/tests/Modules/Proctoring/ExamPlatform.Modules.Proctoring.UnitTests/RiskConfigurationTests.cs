using ExamPlatform.Modules.Proctoring.Application.Queries;
using ExamPlatform.Modules.Proctoring.Application;
using ExamPlatform.Modules.Proctoring.Domain.Exceptions;
using ExamPlatform.Modules.Proctoring.Endpoints;

namespace ExamPlatform.Modules.Proctoring.UnitTests;

/// <summary>The filter the queue accepts and the configuration the host checks at start-up.</summary>
public class RiskConfigurationTests
{
    [Theory]
    [InlineData(null, RiskFlagFilter.Open)]
    [InlineData("", RiskFlagFilter.Open)]
    [InlineData("open", RiskFlagFilter.Open)]
    [InlineData(" Reviewed ", RiskFlagFilter.Reviewed)]
    [InlineData("DISMISSED", RiskFlagFilter.Dismissed)]
    [InlineData("all", RiskFlagFilter.All)]
    public void AFilterName_IsReadWhateverItsCaseOrSpacing(string? text, RiskFlagFilter expected) =>
        Assert.Equal(expected, RiskFlagFilterText.Parse(text));

    [Theory]
    [InlineData("closed")]
    [InlineData("5")]
    public void AnUnknownFilter_IsRefused(string text) =>
        Assert.Throws<InvalidRiskFilterError>(() => RiskFlagFilterText.Parse(text));

    [Fact]
    public void TheShippedDefaults_PassTheValidation()
    {
        var result = new RiskScoringOptionsValidator().Validate(null, new RiskScoringOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AFlagThresholdOfZero_FailsTheValidation_NamingTheSection()
    {
        var result = new RiskScoringOptionsValidator().Validate(null, new RiskScoringOptions { FlagThreshold = 0 });

        Assert.False(result.Succeeded);
        Assert.Contains(RiskScoringOptions.SectionName, result.FailureMessage);
    }

    [Fact]
    public void ASharerLimitBelowTwo_FailsTheValidation()
    {
        var result = new RiskScoringOptionsValidator().Validate(null, new RiskScoringOptions { MaxSharersPerAnswer = 1 });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void ABadRule_IsRefusedByTheDomainWhenTheOptionsBecomeAPolicy() =>
        Assert.Throws<InvalidRiskPolicyError>(() =>
            new RiskScoringOptions { FocusDepartures = new RiskScoringOptions.RuleOptions { Threshold = 3, Weight = -2 } }.ToPolicy());
}
