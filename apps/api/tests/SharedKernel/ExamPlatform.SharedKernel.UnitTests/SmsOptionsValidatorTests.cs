using ExamPlatform.SharedKernel.Infrastructure.Sms;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// The startup check for SMS (FR-39). Switched off it demands nothing, so the settings can be put in place first. Switched on it demands a
/// provider the platform has an adapter for, a registered sender ID and a DLT template ID for each template, and names what is missing.
/// </summary>
public class SmsOptionsValidatorTests
{
    private const string BuiltInProvider = "Acme";

    private static readonly SmsOptionsValidator Validator = new([BuiltInProvider]);

    private static SmsOptions CompleteSettings() => new()
    {
        Enabled = true,
        Provider = BuiltInProvider,
        SenderId = "EXMPLT",
        Templates = new(StringComparer.OrdinalIgnoreCase) { ["Notice"] = "1107000000000000001" },
    };

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void Validate_WhenTheSwitchIsOff_DemandsNothing(bool? enabled)
    {
        var result = Validator.Validate(null, new SmsOptions { Enabled = enabled });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenOnWithCompleteSettings_Succeeds()
    {
        var result = Validator.Validate(null, CompleteSettings());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenOnWithNothingSet_NamesEachMissingSetting()
    {
        var result = Validator.Validate(null, new SmsOptions { Enabled = true });

        Assert.False(result.Succeeded);
        Assert.Contains("Sms:Provider", result.FailureMessage);
        Assert.Contains("Sms:SenderId", result.FailureMessage);
        Assert.Contains("Sms:Templates", result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenOnWithAProviderNoAdapterIsBuiltFor_RefusesIt()
    {
        var settings = CompleteSettings();
        settings.Provider = "Unbuilt";

        var result = Validator.Validate(null, settings);

        Assert.False(result.Succeeded);
        Assert.Contains("'Unbuilt'", result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenNoProviderIsBuiltIn_RefusesEvenACompleteSetup()
    {
        var result = new SmsOptionsValidator([]).Validate(null, CompleteSettings());

        Assert.False(result.Succeeded);
        Assert.Contains("'Acme'", result.FailureMessage);
    }

    [Fact]
    public void Validate_MatchesTheProviderNameIgnoringCase()
    {
        var settings = CompleteSettings();
        settings.Provider = "ACME";

        Assert.True(Validator.Validate(null, settings).Succeeded);
    }

    [Fact]
    public void Validate_WhenATemplateHasNoDltId_NamesTemplates()
    {
        var settings = CompleteSettings();
        settings.Templates["Notice"] = " ";

        var result = Validator.Validate(null, settings);

        Assert.False(result.Succeeded);
        Assert.Contains("Sms:Templates", result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenNoTemplateIsRegistered_NamesTemplates()
    {
        var settings = CompleteSettings();
        settings.Templates.Clear();

        var result = Validator.Validate(null, settings);

        Assert.False(result.Succeeded);
        Assert.Contains("Sms:Templates", result.FailureMessage);
    }
}
