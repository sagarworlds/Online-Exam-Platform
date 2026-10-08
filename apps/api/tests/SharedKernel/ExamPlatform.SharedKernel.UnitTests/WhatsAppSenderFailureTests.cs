using System.Net;
using System.Text;
using System.Text.Json;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// What the sender reports when WhatsApp refuses a message, in the shapes Meta's API actually answers with, and how it sends a plain text
/// message: an operator running the test is told the real reason, and the number goes to Meta in the form Meta recommends.
/// </summary>
public class WhatsAppSenderFailureTests
{
    private const string AccessToken = "EAAG-test-access-token";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        public Uri? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static WhatsAppOptions Configured() => new() { Enabled = true, AccessToken = AccessToken, PhoneNumberId = "1234567890" };

    private static WhatsAppCloudApiSender SenderFor(StubHandler handler, WhatsAppOptions? options = null) =>
        new(new HttpClient(handler), Options.Create(options ?? Configured()), NullLogger<WhatsAppCloudApiSender>.Instance);

    private static HttpResponseMessage Refused(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static readonly WhatsAppTextMessage Hello = new("919876543210", "Hello from the exam platform");

    private static readonly WhatsAppTemplateMessage Code = WhatsAppMessages.AuthenticationCode("919876543210", "exam_login_code", "en", "123456");

    // ---- sending text --------------------------------------------------------------------------------

    [Fact]
    public async Task SendTextAsync_PostsAPlainTextMessage_ToTheSameEndpoint_WithTheNumberItsPlus_AndNoLinkPreview()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"messages":[{"id":"wamid.TEXT1"}]}""", Encoding.UTF8, "application/json"),
        });

        var result = await SenderFor(handler).SendTextAsync(Hello, CancellationToken.None);

        Assert.True(result.Sent);
        Assert.Equal("wamid.TEXT1", result.MessageId);
        Assert.Null(result.Failure);
        Assert.Equal("https://graph.facebook.com/v23.0/1234567890/messages", handler.LastUri!.ToString());
        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        Assert.Equal("whatsapp", body.GetProperty("messaging_product").GetString());
        Assert.Equal("individual", body.GetProperty("recipient_type").GetString());
        Assert.Equal("+919876543210", body.GetProperty("to").GetString());
        Assert.Equal("text", body.GetProperty("type").GetString());
        Assert.Equal("Hello from the exam platform", body.GetProperty("text").GetProperty("body").GetString());
        Assert.False(body.GetProperty("text").GetProperty("preview_url").GetBoolean());
        Assert.False(body.TryGetProperty("template", out _));
    }

    [Fact]
    public async Task ATemplateMessage_AlsoGoesWithThePlus()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        await SenderFor(handler).SendTemplateAsync(Code, CancellationToken.None);

        Assert.Equal("+919876543210", JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!).GetProperty("to").GetString());
    }

    [Fact]
    public async Task ANumberAlreadyWithAPlus_IsNotGivenASecondOne()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        await SenderFor(handler).SendTextAsync(new WhatsAppTextMessage("+919876543210", "x"), CancellationToken.None);

        Assert.Equal("+919876543210", JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!).GetProperty("to").GetString());
    }

    [Fact]
    public async Task TheMessageIsSentAsWritten_QuotesAndNewlinesIncluded()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        const string Written = "She said \"hi\"\nSecond line — with a dash and 日本語";

        await SenderFor(handler).SendTextAsync(new WhatsAppTextMessage("919876543210", Written), CancellationToken.None);

        Assert.Equal(Written, JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!).GetProperty("text").GetProperty("body").GetString());
    }

    // ---- why a send was refused ----------------------------------------------------------------------

    [Fact]
    public async Task AnInvalidToken_IsReportedAsOne_WithMetasWords()
    {
        var handler = new StubHandler(_ => Refused(
            HttpStatusCode.Unauthorized,
            """{"error":{"message":"Invalid OAuth access token - Cannot parse access token","type":"OAuthException","code":190,"fbtrace_id":"AbC123"}}"""));

        var result = await SenderFor(handler).SendTextAsync(Hello, CancellationToken.None);

        Assert.False(result.Sent);
        var failure = result.Failure!;
        Assert.Equal(WhatsAppFailureKind.InvalidToken, failure.Kind);
        Assert.Equal(190, failure.MetaCode);
        Assert.Equal(401, failure.HttpStatus);
        Assert.Equal("Invalid OAuth access token - Cannot parse access token (trace id AbC123)", failure.MetaMessage);
        Assert.Contains("is not a token", failure.Explanation, StringComparison.Ordinal);
        // Neither the token nor the number is ever echoed back.
        Assert.DoesNotContain(AccessToken, failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("919876543210", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWrongPhoneNumberId_IsReportedAsOne()
    {
        var handler = new StubHandler(_ => Refused(
            HttpStatusCode.BadRequest,
            """{"error":{"message":"Unsupported post request. Object with ID '1234567890' does not exist, cannot be loaded due to missing permissions, or does not support this operation. Please read the Graph API documentation","type":"GraphMethodException","code":100,"error_subcode":33,"fbtrace_id":"A1"}}"""));

        var failure = (await SenderFor(handler).SendTextAsync(Hello, CancellationToken.None)).Failure!;

        Assert.Equal(WhatsAppFailureKind.WrongPhoneNumberId, failure.Kind);
        Assert.Equal(100, failure.MetaCode);
    }

    [Fact]
    public async Task ARecipientNotOnTheTestNumbersList_ComesBackWithMetasDetails()
    {
        var handler = new StubHandler(_ => Refused(
            HttpStatusCode.BadRequest,
            """{"error":{"message":"(#131030) Recipient phone number not in allowed list","type":"OAuthException","code":131030,"error_data":{"messaging_product":"whatsapp","details":"Recipient phone number not in allowed list: Add recipient phone number to recipient list and try again."},"fbtrace_id":"Z9"}}"""));

        var failure = (await SenderFor(handler).SendTextAsync(Hello, CancellationToken.None)).Failure!;

        Assert.Equal(WhatsAppFailureKind.RecipientNotAllowed, failure.Kind);
        Assert.Contains("(#131030) Recipient phone number not in allowed list", failure.MetaMessage, StringComparison.Ordinal);
        Assert.Contains("Add recipient phone number to recipient list", failure.MetaMessage, StringComparison.Ordinal);
        Assert.Contains("trace id Z9", failure.MetaMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingTemplate_NamesTheTemplateAndLanguageThatWereSent()
    {
        var handler = new StubHandler(_ => Refused(
            HttpStatusCode.NotFound,
            """{"error":{"message":"(#132001) Template name does not exist in the translation","type":"OAuthException","code":132001,"error_data":{"messaging_product":"whatsapp","details":"template name (exam_login_code) does not exist in en"},"fbtrace_id":"T1"}}"""));

        var failure = (await SenderFor(handler).SendTemplateAsync(Code, CancellationToken.None)).Failure!;

        Assert.Equal(WhatsAppFailureKind.TemplateNotFound, failure.Kind);
        Assert.Contains("\"exam_login_code\"", failure.Explanation, StringComparison.Ordinal);
        Assert.Contains("\"en\"", failure.Explanation, StringComparison.Ordinal);
        Assert.Equal(404, failure.HttpStatus);
    }

    [Fact]
    public async Task ABadApiVersion_IsReportedAsABadAddress()
    {
        var handler = new StubHandler(_ => Refused(
            HttpStatusCode.BadRequest,
            """{"error":{"message":"Unknown path components: /v99.0/1234567890/messages","type":"OAuthException","code":2500,"fbtrace_id":"U1"}}"""));
        var options = Configured();
        options.ApiVersion = "v99.0";

        var failure = (await SenderFor(handler, options).SendTextAsync(Hello, CancellationToken.None)).Failure!;

        Assert.Equal(WhatsAppFailureKind.WrongApiAddress, failure.Kind);
    }

    [Fact]
    public async Task AnOutcomeWithNoJsonBody_IsClassifiedByItsStatus_AndTheBodyIsNotRepeated()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>Bad gateway for 919876543210</html>"),
        });

        var failure = (await SenderFor(handler).SendTextAsync(Hello, CancellationToken.None)).Failure!;

        Assert.Equal(WhatsAppFailureKind.ServiceUnavailable, failure.Kind);
        Assert.Null(failure.MetaCode);
        Assert.Null(failure.MetaMessage);
        Assert.DoesNotContain("919876543210", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnlistedCode_IsPassedOnInMetasWords()
    {
        var handler = new StubHandler(_ => Refused(
            HttpStatusCode.BadRequest, """{"error":{"message":"(#555555) Something new","code":555555}}"""));

        var failure = (await SenderFor(handler).SendTextAsync(Hello, CancellationToken.None)).Failure!;

        Assert.Equal(WhatsAppFailureKind.Rejected, failure.Kind);
        Assert.Equal("(#555555) Something new", failure.MetaMessage);
    }

    [Fact]
    public async Task WhenTheNetworkFails_TheHostIsNamed_AndNotTheToken()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("Name resolution failed", new System.Net.Sockets.SocketException(11001)));

        var failure = (await SenderFor(handler).SendTextAsync(Hello, CancellationToken.None)).Failure!;

        Assert.Equal(WhatsAppFailureKind.Unreachable, failure.Kind);
        Assert.Contains("graph.facebook.com", failure.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessToken, failure.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("graph.facebook.com")]
    [InlineData("ftp://graph.facebook.com")]
    [InlineData("not a url")]
    public async Task ABaseUrlThatIsNotAWebAddress_IsReportedAsSuch_InsteadOfThrowing(string baseUrl)
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call out"));
        var options = Configured();
        options.BaseUrl = baseUrl;

        var text = await SenderFor(handler, options).SendTextAsync(Hello, CancellationToken.None);
        var template = await SenderFor(handler, options).SendTemplateAsync(Code, CancellationToken.None);

        Assert.All([text, template], result =>
        {
            Assert.False(result.Sent);
            Assert.Equal(WhatsAppFailureKind.WrongApiAddress, result.Failure!.Kind);
            Assert.Contains("WhatsApp__BaseUrl", result.Failure.Explanation, StringComparison.Ordinal);
            Assert.Contains("https://", result.Failure.Explanation, StringComparison.Ordinal);
        });
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task WhenWhatsAppDoesNotAnswerInTime_ItIsReportedAsTimedOut()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timed out"));

        var failure = (await SenderFor(handler).SendTextAsync(Hello, CancellationToken.None)).Failure!;

        Assert.Equal(WhatsAppFailureKind.TimedOut, failure.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task WithTheSwitchOff_ATextSendSaysSo_AndCallsNobody(bool? enabled)
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call out"));
        var options = Configured();
        options.Enabled = enabled;

        var result = await SenderFor(handler, options).SendTextAsync(Hello, CancellationToken.None);

        Assert.False(result.Sent);
        Assert.Equal(WhatsAppFailureKind.SwitchedOff, result.Failure!.Kind);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task WithoutCredentials_BothKindsOfSendNameWhatIsMissing()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call out"));
        var options = new WhatsAppOptions { Enabled = true, AccessToken = "", PhoneNumberId = null };

        var text = (await SenderFor(handler, options).SendTextAsync(Hello, CancellationToken.None)).Failure!;
        var template = (await SenderFor(handler, options).SendTemplateAsync(Code, CancellationToken.None)).Failure!;

        Assert.All([text, template], f =>
        {
            Assert.Equal(WhatsAppFailureKind.NotConfigured, f.Kind);
            Assert.Contains("WhatsApp__AccessToken", f.Explanation, StringComparison.Ordinal);
            Assert.Contains("WhatsApp__PhoneNumberId", f.Explanation, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ASuccessfulSend_HasNoFailure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"messages":[{"id":"wamid.OK"}]}""") });

        var result = await SenderFor(handler).SendTemplateAsync(Code, CancellationToken.None);

        Assert.True(result.Sent);
        Assert.Null(result.Failure);
    }

    // ---- what the webhook says later ---------------------------------------------------------------------

    [Fact]
    public void AFailedDeliveryReport_CarriesMetasDetails()
    {
        const string Body = """
            {"entry":[{"changes":[{"value":{"statuses":[{"id":"wamid.X","status":"failed","timestamp":"1","recipient_id":"919876543210",
            "errors":[{"code":131047,"title":"Re-engagement message","message":"Re-engagement message","error_data":{"details":"Message failed to send because more than 24 hours have passed since the customer last replied to this number."},"href":"https://x"}]}]}}]}]}
            """;

        var status = Assert.IsType<WhatsAppDeliveryStatus>(Assert.Single(WhatsAppWebhookPayload.Parse(Body)));

        Assert.Equal(131047, status.ErrorCode);
        Assert.Equal("Re-engagement message", status.ErrorTitle);
        Assert.StartsWith("Message failed to send because more than 24 hours", status.ErrorDetails, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeliveryReportWithNoError_HasNoDetails()
    {
        const string Body = """{"entry":[{"changes":[{"value":{"statuses":[{"id":"wamid.X","status":"delivered","recipient_id":"1"}]}}]}]}""";

        var status = Assert.IsType<WhatsAppDeliveryStatus>(Assert.Single(WhatsAppWebhookPayload.Parse(Body)));

        Assert.Null(status.ErrorCode);
        Assert.Null(status.ErrorDetails);
    }

    [Fact]
    public void AFailedReport_BecomesTheSameExplanationASynchronousRefusalWouldHave()
    {
        var tracker = new InMemoryWhatsAppDeliveryTracker(new FixedClock());
        tracker.Record(new WhatsAppDeliveryStatus(
            "wamid.X", "failed", "919876543210", 131047, "Re-engagement message", "More than 24 hours have passed."));

        var failure = tracker.Find("wamid.X")!.Failure!;

        Assert.Equal(WhatsAppFailureKind.ReEngagementRequired, failure.Kind);
        Assert.Equal("Re-engagement message More than 24 hours have passed.", failure.MetaMessage);
    }

    private sealed class FixedClock : ExamPlatform.SharedKernel.Application.Clock
    {
        public DateTime UtcNow => new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    }
}
