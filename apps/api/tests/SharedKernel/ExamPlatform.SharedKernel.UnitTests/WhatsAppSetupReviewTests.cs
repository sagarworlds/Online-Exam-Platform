using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>What the administrator's WhatsApp test says about the configuration, worked out from the settings alone.</summary>
public class WhatsAppSetupReviewTests
{
    private static WhatsAppOptions Complete() => new()
    {
        Enabled = true,
        AccessToken = "EAAG-secret-token",
        PhoneNumberId = "1234567890",
        OtpTemplateName = "exam_login_code",
        AppSecret = "app-secret-value",
        WebhookVerifyToken = "verify-token-value",
    };

    private static WhatsAppSettingStatus Setting(WhatsAppSetupReview review, string name) => review.Settings.Single(s => s.Setting == name);

    [Fact]
    public void AFullyConfiguredSetup_HasNoProblems_AndCanSendAndTrack()
    {
        var review = WhatsAppSetup.Review(Complete(), phoneCodesUseWhatsApp: true, inviteTemplateName: "exam_invite");

        Assert.True(review.Enabled);
        Assert.True(review.CanSendMessages);
        Assert.True(review.CanSendTemplate);
        Assert.True(review.CanTrackDelivery);
        Assert.True(review.SignInCodesUseWhatsApp);
        Assert.True(review.InviteCodesUseWhatsApp);
        Assert.Empty(review.Problems);
        Assert.Empty(review.Notes);
    }

    [Fact]
    public void NothingConfigured_SaysEverythingThatIsMissing()
    {
        var review = WhatsAppSetup.Review(new WhatsAppOptions(), phoneCodesUseWhatsApp: false, inviteTemplateName: null);

        Assert.False(review.Enabled);
        Assert.False(review.CanSendMessages);
        Assert.False(review.CanSendTemplate);
        Assert.Contains(review.Problems, p => p.Contains("switched off", StringComparison.Ordinal));
        Assert.Contains(review.Problems, p => p.Contains("WhatsApp__AccessToken", StringComparison.Ordinal));
        Assert.Contains(review.Problems, p => p.Contains("WhatsApp__PhoneNumberId", StringComparison.Ordinal));
        Assert.Contains(review.Notes, n => n.Contains("WhatsApp__OtpTemplateName", StringComparison.Ordinal));
        Assert.Contains(review.Notes, n => n.Contains("Identity__OtpDelivery__PhoneProvider", StringComparison.Ordinal));
        Assert.Contains(review.Notes, n => n.Contains("Invite__WhatsApp__TemplateName", StringComparison.Ordinal));
        Assert.Contains(review.Notes, n => n.Contains("Delivery reports are off", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void WithTheSwitchOff_NothingCanBeSent_EvenWhenFullyConfigured(bool? enabled)
    {
        var options = Complete();
        options.Enabled = enabled;

        var review = WhatsAppSetup.Review(options, true, null);

        Assert.False(review.CanSendMessages);
        Assert.False(review.CanSendTemplate);
        Assert.Single(review.Problems);
        Assert.Contains("switched off", review.Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutATemplate_MessagesCanBeSentButTheTemplateCannot()
    {
        var options = Complete();
        options.OtpTemplateName = " ";

        var review = WhatsAppSetup.Review(options, true, null);

        Assert.True(review.CanSendMessages);
        Assert.False(review.CanSendTemplate);
        Assert.Empty(review.Problems);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void HalfAWebhook_IsAProblem(bool hasSecret, bool hasVerifyToken)
    {
        var options = Complete();
        options.AppSecret = hasSecret ? "secret" : null;
        options.WebhookVerifyToken = hasVerifyToken ? "token" : null;

        var review = WhatsAppSetup.Review(options, true, null);

        Assert.False(review.CanTrackDelivery);
        Assert.Contains(review.Problems, p => p.Contains("needs both", StringComparison.Ordinal));
    }

    [Fact]
    public void NoWebhookAtAll_IsAnOptionalNote_NotAProblem()
    {
        var options = Complete();
        options.AppSecret = null;
        options.WebhookVerifyToken = null;

        var review = WhatsAppSetup.Review(options, true, null);

        Assert.False(review.CanTrackDelivery);
        Assert.Empty(review.Problems);
        Assert.Contains(review.Notes, n => n.Contains("Delivery reports are off", StringComparison.Ordinal));
    }

    [Fact]
    public void NoSecretIsEverShown_ButNonSecretValuesAre()
    {
        var review = WhatsAppSetup.Review(Complete(), true, "exam_invite");

        foreach (var secret in new[] { "WhatsApp__AccessToken", "WhatsApp__AppSecret", "WhatsApp__WebhookVerifyToken" })
        {
            Assert.True(Setting(review, secret).IsSet);
            Assert.Null(Setting(review, secret).Value);
        }

        Assert.DoesNotContain("EAAG-secret-token", string.Join('|', review.Settings.Select(s => s.Value).Concat(review.Problems).Concat(review.Notes)), StringComparison.Ordinal);
        Assert.Equal("1234567890", Setting(review, "WhatsApp__PhoneNumberId").Value);
        Assert.Equal("exam_login_code", Setting(review, "WhatsApp__OtpTemplateName").Value);
        Assert.Equal("exam_invite", Setting(review, "Invite__WhatsApp__TemplateName").Value);
        Assert.Equal("en", Setting(review, "WhatsApp__OtpTemplateLanguage").Value);
        Assert.Equal("91", Setting(review, "WhatsApp__DefaultCountryCode").Value);
        Assert.Equal("true", Setting(review, "WhatsApp__Enabled").Value);
        Assert.Equal("WhatsApp", Setting(review, "Identity__OtpDelivery__PhoneProvider").Value);
    }

    [Fact]
    public void TheMasterSwitch_ReportsFalseWhenExplicitlyOff_AndNothingWhenUnset()
    {
        Assert.Equal("false", Setting(WhatsAppSetup.Review(new WhatsAppOptions { Enabled = false }, false, null), "WhatsApp__Enabled").Value);
        Assert.Null(Setting(WhatsAppSetup.Review(new WhatsAppOptions(), false, null), "WhatsApp__Enabled").Value);
    }

    [Fact]
    public void ABlankCountryCode_IsNoted()
    {
        var options = Complete();
        options.DefaultCountryCode = "";

        var review = WhatsAppSetup.Review(options, true, null);

        Assert.Contains(review.Notes, n => n.Contains("WhatsApp__DefaultCountryCode is blank", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyTheEssentialSettings_AreMarkedRequired()
    {
        var required = WhatsAppSetup.Review(Complete(), true, null).Settings.Where(s => s.Required).Select(s => s.Setting);

        Assert.Equal(["WhatsApp__Enabled", "WhatsApp__AccessToken", "WhatsApp__PhoneNumberId"], required);
    }
}
