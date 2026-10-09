using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// The table that turns what Meta reported into what an operator is told. It is classified on Meta's error code, as Meta advises,
/// and the codes below were checked against Meta's error-code reference.
/// </summary>
public class WhatsAppErrorGuideTests
{
    [Theory]
    [InlineData(190, WhatsAppFailureKind.InvalidToken)]
    [InlineData(10, WhatsAppFailureKind.PermissionDenied)]
    [InlineData(3, WhatsAppFailureKind.PermissionDenied)]
    [InlineData(131005, WhatsAppFailureKind.PermissionDenied)]
    [InlineData(200, WhatsAppFailureKind.PermissionDenied)]
    [InlineData(250, WhatsAppFailureKind.PermissionDenied)]
    [InlineData(299, WhatsAppFailureKind.PermissionDenied)]
    [InlineData(33, WhatsAppFailureKind.WrongPhoneNumberId)]
    [InlineData(100, WhatsAppFailureKind.InvalidRequest)]
    [InlineData(131008, WhatsAppFailureKind.InvalidRequest)]
    [InlineData(135000, WhatsAppFailureKind.InvalidRequest)]
    [InlineData(2500, WhatsAppFailureKind.WrongApiAddress)]
    [InlineData(131009, WhatsAppFailureKind.InvalidRecipient)]
    [InlineData(131021, WhatsAppFailureKind.InvalidRecipient)]
    [InlineData(131030, WhatsAppFailureKind.RecipientNotAllowed)]
    [InlineData(131047, WhatsAppFailureKind.ReEngagementRequired)]
    [InlineData(131026, WhatsAppFailureKind.NotOnWhatsApp)]
    [InlineData(131037, WhatsAppFailureKind.NumberNotRegistered)]
    [InlineData(131045, WhatsAppFailureKind.NumberNotRegistered)]
    [InlineData(133010, WhatsAppFailureKind.NumberNotRegistered)]
    [InlineData(131042, WhatsAppFailureKind.PaymentProblem)]
    [InlineData(131031, WhatsAppFailureKind.AccountRestricted)]
    [InlineData(130497, WhatsAppFailureKind.AccountRestricted)]
    [InlineData(368, WhatsAppFailureKind.AccountRestricted)]
    [InlineData(4, WhatsAppFailureKind.RateLimited)]
    [InlineData(80007, WhatsAppFailureKind.RateLimited)]
    [InlineData(130429, WhatsAppFailureKind.RateLimited)]
    [InlineData(131056, WhatsAppFailureKind.RateLimited)]
    [InlineData(131048, WhatsAppFailureKind.RateLimited)]
    [InlineData(1, WhatsAppFailureKind.ServiceUnavailable)]
    [InlineData(2, WhatsAppFailureKind.ServiceUnavailable)]
    [InlineData(131016, WhatsAppFailureKind.ServiceUnavailable)]
    [InlineData(133004, WhatsAppFailureKind.ServiceUnavailable)]
    [InlineData(131000, WhatsAppFailureKind.ServiceUnavailable)]
    [InlineData(131057, WhatsAppFailureKind.ServiceUnavailable)]
    [InlineData(132000, WhatsAppFailureKind.TemplateMismatch)]
    [InlineData(132005, WhatsAppFailureKind.TemplateMismatch)]
    [InlineData(132012, WhatsAppFailureKind.TemplateMismatch)]
    [InlineData(132001, WhatsAppFailureKind.TemplateNotFound)]
    [InlineData(132015, WhatsAppFailureKind.TemplateUnavailable)]
    [InlineData(132016, WhatsAppFailureKind.TemplateUnavailable)]
    [InlineData(131049, WhatsAppFailureKind.Rejected)]
    public void EveryCodeInTheTable_IsClassifiedByItsCode(int code, WhatsAppFailureKind expected)
    {
        var failure = WhatsAppErrorGuide.Explain(null, code, "whatever Meta said");

        Assert.Equal(expected, failure.Kind);
        Assert.Equal(code, failure.MetaCode);
        Assert.False(string.IsNullOrWhiteSpace(failure.Explanation));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public void TheCodeDecides_NotTheHttpStatus(int httpStatus)
    {
        // Meta publishes no status per code and the same code has been seen with different ones.
        Assert.Equal(WhatsAppFailureKind.ReEngagementRequired, WhatsAppErrorGuide.Explain(httpStatus, 131047, null).Kind);
        Assert.Equal(WhatsAppFailureKind.RecipientNotAllowed, WhatsAppErrorGuide.Explain(httpStatus, 131030, null).Kind);
    }

    [Fact]
    public void AnExpiredToken_IsToldApart_FromOneThatIsNotAToken()
    {
        var expired = WhatsAppErrorGuide.Explain(401, 190, "Error validating access token: Session has expired on Tuesday", subcode: 463);
        var unparseable = WhatsAppErrorGuide.Explain(401, 190, "Invalid OAuth access token - Cannot parse access token");
        var other = WhatsAppErrorGuide.Explain(401, 190, "Error validating access token: The user has not authorized the application");

        Assert.All([expired, unparseable, other], f => Assert.Equal(WhatsAppFailureKind.InvalidToken, f.Kind));
        Assert.Contains("expired", expired.Explanation, StringComparison.Ordinal);
        Assert.Contains("is not a token", unparseable.Explanation, StringComparison.Ordinal);
        Assert.Contains("revoked", other.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpiryIsRecognisedFromTheSubcodeAlone_OrFromTheWordsAlone()
    {
        Assert.Contains("expired", WhatsAppErrorGuide.Explain(401, 190, "anything", subcode: 463).Explanation, StringComparison.Ordinal);
        Assert.Contains("expired", WhatsAppErrorGuide.Explain(401, 190, "Session has expired on x").Explanation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Unsupported post request. Object with ID '123' does not exist, cannot be loaded due to missing permissions, or does not support this operation.", null)]
    [InlineData("anything", 33)]
    public void AWrongPhoneNumberId_IsRecognisedAmongTheOther100s(string message, int? subcode)
    {
        var failure = WhatsAppErrorGuide.Explain(400, 100, message, subcode: subcode);

        Assert.Equal(WhatsAppFailureKind.WrongPhoneNumberId, failure.Kind);
        Assert.Contains("WhatsApp__PhoneNumberId", failure.Explanation, StringComparison.Ordinal);
        Assert.Contains("not the phone number", failure.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrdinary100_IsAnInvalidRequest_NotAWrongPhoneNumberId()
    {
        Assert.Equal(WhatsAppFailureKind.InvalidRequest, WhatsAppErrorGuide.Explain(400, 100, "(#100) Invalid parameter").Kind);
    }

    [Fact]
    public void ATemplateProblem_NamesTheTemplateAndLanguageItWasSentWith()
    {
        var missing = WhatsAppErrorGuide.Explain(404, 132001, null, "exam_login_code", "en_US");
        var mismatch = WhatsAppErrorGuide.Explain(400, 132000, null, "exam_login_code", "en");
        var paused = WhatsAppErrorGuide.Explain(400, 132015, null, "exam_login_code", "en");

        Assert.Contains("\"exam_login_code\"", missing.Explanation, StringComparison.Ordinal);
        Assert.Contains("\"en_US\"", missing.Explanation, StringComparison.Ordinal);
        Assert.Contains("WhatsApp__OtpTemplateLanguage", missing.Explanation, StringComparison.Ordinal);
        Assert.Contains("\"exam_login_code\"", mismatch.Explanation, StringComparison.Ordinal);
        Assert.Contains("\"exam_login_code\"", paused.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void ATemplateProblem_WithNoTemplateKnown_StillReadsSensibly()
    {
        var missing = WhatsAppErrorGuide.Explain(404, 132001, null);

        Assert.Contains("(none)", missing.Explanation, StringComparison.Ordinal);
        Assert.StartsWith("The template", WhatsAppErrorGuide.Explain(400, 132015, null).Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReEngagementError_OffersBothWaysOut()
    {
        var explanation = WhatsAppErrorGuide.Explain(null, 131047, "Re-engagement message").Explanation;

        Assert.Contains("24 hours", explanation, StringComparison.Ordinal);
        Assert.Contains("send any message to your WhatsApp number", explanation, StringComparison.Ordinal);
        Assert.Contains("sign-in code template", explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTestNumberError_ExplainsTheAllowedList_AndTheWayToARealNumber()
    {
        var explanation = WhatsAppErrorGuide.Explain(400, 131030, "(#131030) Recipient phone number not in allowed list").Explanation;

        Assert.Contains("Manage phone number list", explanation, StringComparison.Ordinal);
        Assert.Contains("WhatsApp__PhoneNumberId", explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void ABadApiAddress_NamesBothSettingsAndTheDefault()
    {
        var explanation = WhatsAppErrorGuide.Explain(400, 2500, "Unknown path components: /v99.0/1/messages").Explanation;

        Assert.Contains("WhatsApp__ApiVersion", explanation, StringComparison.Ordinal);
        Assert.Contains("WhatsApp__BaseUrl", explanation, StringComparison.Ordinal);
        Assert.Contains("https://graph.facebook.com", explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void ACode0_IsATokenProblem_OnlyWhenItSaysSo()
    {
        Assert.Equal(WhatsAppFailureKind.InvalidToken, WhatsAppErrorGuide.Explain(401, 0, "We were unable to authenticate the app user").Kind);
        Assert.Equal(WhatsAppFailureKind.InvalidToken, WhatsAppErrorGuide.Explain(401, 0, null).Kind);
        Assert.Equal(WhatsAppFailureKind.Rejected, WhatsAppErrorGuide.Explain(400, 0, "something else").Kind);
    }

    [Fact]
    public void AnUnlistedCode_IsReportedAsRejected_WithMetasOwnWords_NotGuessedAt()
    {
        var failure = WhatsAppErrorGuide.Explain(400, 999999, "(#999999) A brand new problem");

        Assert.Equal(WhatsAppFailureKind.Rejected, failure.Kind);
        Assert.Contains("999999", failure.Explanation, StringComparison.Ordinal);
        Assert.Equal("(#999999) A brand new problem", failure.MetaMessage);
        Assert.Equal(400, failure.HttpStatus);
    }

    [Fact]
    public void WithNoCode_TheStatusIsTheFallback()
    {
        Assert.Equal(WhatsAppFailureKind.InvalidToken, WhatsAppErrorGuide.Explain(401, null, null).Kind);
        Assert.Equal(WhatsAppFailureKind.ServiceUnavailable, WhatsAppErrorGuide.Explain(502, null, "<html>Bad gateway</html>").Kind);
        Assert.Equal(WhatsAppFailureKind.Rejected, WhatsAppErrorGuide.Explain(400, null, null).Kind);
        Assert.Equal(WhatsAppFailureKind.Rejected, WhatsAppErrorGuide.Explain(null, null, null).Kind);
    }

    [Fact]
    public void EveryExplanation_IsAnAdviceNotAnEcho_AndNeverContainsTheMetaMessage()
    {
        // The message is carried beside the explanation so the operator sees both; repeating it would hide which is which.
        foreach (var code in new[] { 190, 10, 100, 2500, 131030, 131047, 131026, 132001 })
        {
            var failure = WhatsAppErrorGuide.Explain(400, code, "UNIQUE-META-WORDS");

            Assert.DoesNotContain("UNIQUE-META-WORDS", failure.Explanation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Describe_JoinsMessageDetailsAndTraceId_WithoutRepeatingAnything()
    {
        Assert.Equal(
            "(#132000) Number of parameters does not match body: 0 of 1 (trace id AbC)",
            WhatsAppErrorGuide.Describe("(#132000) Number of parameters does not match", "body: 0 of 1", "AbC"));
        Assert.Equal("Same words", WhatsAppErrorGuide.Describe("Same words", " same WORDS ", null));
        Assert.Equal("Only details", WhatsAppErrorGuide.Describe(null, "Only details", null));
        Assert.Equal("(trace id T1)", WhatsAppErrorGuide.Describe("  ", null, "T1"));
        Assert.Null(WhatsAppErrorGuide.Describe(null, " ", null));
    }

    [Fact]
    public void TheFactoryFailures_SayWhatToDo()
    {
        Assert.Equal(WhatsAppFailureKind.SwitchedOff, WhatsAppFailure.SwitchedOff().Kind);
        Assert.Contains("WhatsApp__Enabled", WhatsAppFailure.SwitchedOff().Explanation, StringComparison.Ordinal);

        var one = WhatsAppFailure.NotConfigured(["AccessToken"]);
        Assert.Contains("WhatsApp__AccessToken is not set", one.Explanation, StringComparison.Ordinal);
        var two = WhatsAppFailure.NotConfigured(["AccessToken", "PhoneNumberId"]);
        Assert.Contains("WhatsApp__AccessToken, WhatsApp__PhoneNumberId are not set", two.Explanation, StringComparison.Ordinal);

        Assert.Equal(WhatsAppFailureKind.InvalidNumber, WhatsAppFailure.InvalidNumber().Kind);
        Assert.Contains("country code", WhatsAppFailure.InvalidNumber().Explanation, StringComparison.Ordinal);

        var unreachable = WhatsAppFailure.Unreachable("graph.facebook.com", "No such host is known");
        Assert.Equal(WhatsAppFailureKind.Unreachable, unreachable.Kind);
        Assert.Contains("graph.facebook.com", unreachable.Explanation, StringComparison.Ordinal);
        Assert.Contains("No such host is known", unreachable.Explanation, StringComparison.Ordinal);

        Assert.Equal(WhatsAppFailureKind.TimedOut, WhatsAppFailure.TimedOut().Kind);
    }
}
