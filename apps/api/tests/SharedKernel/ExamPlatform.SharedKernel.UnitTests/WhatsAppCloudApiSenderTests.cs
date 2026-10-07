using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// <see cref="WhatsAppCloudApiSender"/> against a stub <see cref="HttpMessageHandler"/>, so what is checked is the request it
/// builds and how it reads the Cloud API's answer, without a real network call.
/// </summary>
public class WhatsAppCloudApiSenderTests
{
    private const string AccessToken = "EAAG-test-access-token";

    private static readonly WhatsAppTemplateMessage OtpMessage = new("919876543210", "exam_login_code", "en", ["123456"], "123456");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private sealed class ListLogger : ILogger<WhatsAppCloudApiSender>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
    }

    private static WhatsAppOptions Configured() => new() { AccessToken = AccessToken, PhoneNumberId = "1234567890" };

    private static WhatsAppCloudApiSender SenderFor(StubHandler handler, WhatsAppOptions? options = null, ILogger<WhatsAppCloudApiSender>? logger = null) =>
        new(new HttpClient(handler), Options.Create(options ?? Configured()), logger ?? new ListLogger());

    private static HttpResponseMessage Accepted(string messageId = "wamid.HBgM123") =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"messaging_product":"whatsapp","contacts":[{"input":"919876543210","wa_id":"919876543210"}],"messages":[{"id":"{{messageId}}"}]}""",
                Encoding.UTF8,
                "application/json"),
        };

    [Fact]
    public async Task SendTemplateAsync_PostsTheTemplateToTheGraphApi_WithTheBearerToken()
    {
        var handler = new StubHandler(_ => Accepted());

        var result = await SenderFor(handler).SendTemplateAsync(OtpMessage, CancellationToken.None);

        Assert.True(result.Sent);
        Assert.Equal("wamid.HBgM123", result.MessageId);
        Assert.Equal("https://graph.facebook.com/v23.0/1234567890/messages", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal(AccessToken, handler.LastRequest.Headers.Authorization.Parameter);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        Assert.Equal("whatsapp", body.GetProperty("messaging_product").GetString());
        Assert.Equal("919876543210", body.GetProperty("to").GetString());
        Assert.Equal("template", body.GetProperty("type").GetString());

        var template = body.GetProperty("template");
        Assert.Equal("exam_login_code", template.GetProperty("name").GetString());
        Assert.Equal("en", template.GetProperty("language").GetProperty("code").GetString());

        var components = template.GetProperty("components");
        Assert.Equal(2, components.GetArrayLength());
        Assert.Equal("body", components[0].GetProperty("type").GetString());
        Assert.Equal("123456", components[0].GetProperty("parameters")[0].GetProperty("text").GetString());

        // An Authentication template's copy-code button is a URL button at index 0, which takes the code again.
        Assert.Equal("button", components[1].GetProperty("type").GetString());
        Assert.Equal("url", components[1].GetProperty("sub_type").GetString());
        Assert.Equal("0", components[1].GetProperty("index").GetString());
        Assert.Equal("123456", components[1].GetProperty("parameters")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendTemplateAsync_WithoutAButtonValue_SendsOnlyTheBody()
    {
        var handler = new StubHandler(_ => Accepted());

        await SenderFor(handler).SendTemplateAsync(OtpMessage with { UrlButtonParameter = null }, CancellationToken.None);

        var components = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!).GetProperty("template").GetProperty("components");
        Assert.Equal("body", Assert.Single(components.EnumerateArray()).GetProperty("type").GetString());
    }

    [Fact]
    public async Task SendTemplateAsync_UsesTheConfiguredAddressAndApiVersion()
    {
        var handler = new StubHandler(_ => Accepted());
        var options = Configured();
        options.BaseUrl = "https://graph.stand-in.test/";
        options.ApiVersion = "v99.0";

        await SenderFor(handler, options).SendTemplateAsync(OtpMessage, CancellationToken.None);

        Assert.Equal("https://graph.stand-in.test/v99.0/1234567890/messages", handler.LastRequest!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(null, "1234567890")]
    [InlineData("", "1234567890")]
    [InlineData(AccessToken, null)]
    [InlineData(AccessToken, "  ")]
    public async Task SendTemplateAsync_WithoutTheAccessTokenOrPhoneNumberId_SendsNothingAndSaysSo(string? token, string? phoneNumberId)
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Should not call out when not configured."));
        var logger = new ListLogger();

        var result = await SenderFor(handler, new WhatsAppOptions { AccessToken = token, PhoneNumberId = phoneNumberId }, logger)
            .SendTemplateAsync(OtpMessage, CancellationToken.None);

        Assert.False(result.Sent);
        Assert.Equal(0, handler.Calls);
        Assert.Contains(logger.Messages, m => m.Contains("not configured", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendTemplateAsync_WhenWhatsAppRefuses_ReportsNotSentWithMetasReason_AndLogsNeitherTheCodeNorTheToken()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"error":{"message":"(#132001) Template name does not exist in the translation","type":"OAuthException","code":132001,"fbtrace_id":"AbC"}}""",
                Encoding.UTF8,
                "application/json"),
        });
        var logger = new ListLogger();

        var result = await SenderFor(handler, logger: logger).SendTemplateAsync(OtpMessage, CancellationToken.None);

        Assert.False(result.Sent);
        Assert.Null(result.MessageId);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains("Template name does not exist", logged, StringComparison.Ordinal);
        Assert.Contains("132001", logged, StringComparison.Ordinal);

        // The code is a credential for the account it signs in, and the token is one for the business's WhatsApp.
        Assert.DoesNotContain("123456", logged, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessToken, logged, StringComparison.Ordinal);
        Assert.DoesNotContain("919876543210", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendTemplateAsync_WhenTheRefusalIsNotJson_ReportsNotSentWithoutRepeatingTheBody()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>Bad gateway for 919876543210</html>"),
        });
        var logger = new ListLogger();

        var result = await SenderFor(handler, logger: logger).SendTemplateAsync(OtpMessage, CancellationToken.None);

        Assert.False(result.Sent);
        Assert.DoesNotContain("919876543210", Assert.Single(logger.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendTemplateAsync_WhenAcceptedWithAnUnreadableAnswer_StillReportsSent()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") });

        var result = await SenderFor(handler).SendTemplateAsync(OtpMessage, CancellationToken.None);

        Assert.True(result.Sent);
        Assert.Null(result.MessageId);
    }

    [Fact]
    public async Task SendTemplateAsync_WhenTheConnectionFails_ReportsNotSent_InsteadOfThrowing()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("DNS failure"));

        var result = await SenderFor(handler).SendTemplateAsync(OtpMessage, CancellationToken.None);

        Assert.False(result.Sent);
    }

    [Fact]
    public async Task SendTemplateAsync_WhenTheRequestTimesOut_ReportsNotSent_InsteadOfThrowing()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("The request timed out."));

        var result = await SenderFor(handler).SendTemplateAsync(OtpMessage, CancellationToken.None);

        Assert.False(result.Sent);
    }

    // What a real handler does when the caller's token is cancelled: it reports the same TaskCanceledException a timeout does.
    private sealed class CancelledByCallerHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("The caller gave up.", null, cancellationToken);
    }

    [Fact]
    public async Task SendTemplateAsync_WhenTheCallerCancels_Throws_UnlikeATimeout()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var sender = new WhatsAppCloudApiSender(new HttpClient(new CancelledByCallerHandler()), Options.Create(Configured()), new ListLogger());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendTemplateAsync(OtpMessage, cancelled.Token));
    }
}
