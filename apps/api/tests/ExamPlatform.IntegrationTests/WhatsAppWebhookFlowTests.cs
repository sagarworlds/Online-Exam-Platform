using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The Host with the WhatsApp webhook configured (app secret and verify token), and a log it can be asked about, since a
/// delivery report's only effect today is a line an operator reads.
/// </summary>
public sealed class WhatsAppWebhookApiFactory : ApiFactory
{
    /// <summary>The app secret the webhook's signatures are checked against.</summary>
    public const string AppSecret = "integration-test-app-secret";

    /// <summary>The verify token Meta must present during set-up.</summary>
    public const string VerifyToken = "integration-test-verify-token";

    /// <summary>What the host has logged, by category.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration =>
        new Dictionary<string, string?>(base.AdditionalConfiguration)
        {
            ["WhatsApp:AppSecret"] = AppSecret,
            ["WhatsApp:WebhookVerifyToken"] = VerifyToken,
        };

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }

    /// <summary>One line the host logged.</summary>
    public sealed record LogEntry(string Category, LogLevel Level, string Message);

    /// <summary>Keeps every line logged, for a test to look through.</summary>
    public sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        /// <summary>Every line logged so far.</summary>
        public IReadOnlyCollection<LogEntry> Entries => _entries;

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        /// <inheritdoc />
        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>
/// The callback URL Meta's WhatsApp webhook is pointed at: the set-up handshake is answered only for the verify token, and a
/// delivery report is believed only if the app secret signed it. Unsigned or mis-signed calls are refused before anything is
/// read from them, since anyone can reach the route.
/// </summary>
public sealed class WhatsAppWebhookFlowTests(WhatsAppWebhookApiFactory factory) : IClassFixture<WhatsAppWebhookApiFactory>
{
    private const string Route = "/v1/webhooks/whatsapp";
    private const string Category = "ExamPlatform.Api.WhatsAppWebhook";

    private static string UniqueId() => $"wamid.{Guid.NewGuid():N}";

    private static HttpContent Signed(string body, string? secret = WhatsAppWebhookApiFactory.AppSecret)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        if (secret is not null)
        {
            content.Headers.TryAddWithoutValidation(
                WhatsAppWebhookSignature.HeaderName, WhatsAppWebhookSignature.Compute(Encoding.UTF8.GetBytes(body), secret));
        }

        return content;
    }

    // A change's value object (braces included), wrapped the way Meta wraps it.
    private static string Call(string value) =>
        """{"object":"whatsapp_business_account","entry":[{"id":"WABA","changes":[{"field":"messages","value":""" + value + """}]}]}""";

    private IEnumerable<WhatsAppWebhookApiFactory.LogEntry> WebhookLogs() =>
        factory.Logs.Entries.Where(e => e.Category == Category);

    // ---- the set-up handshake -----------------------------------------------------------------------

    [Fact]
    public async Task Verification_WithTheRightToken_EchoesTheChallengeAsPlainText()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{Route}?hub.mode=subscribe&hub.verify_token={WhatsAppWebhookApiFactory.VerifyToken}&hub.challenge=1158201444");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1158201444", await response.Content.ReadAsStringAsync());
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("subscribe", "wrong-token")]
    [InlineData("subscribe", "")]
    [InlineData("unsubscribe", WhatsAppWebhookApiFactory.VerifyToken)]
    [InlineData("", WhatsAppWebhookApiFactory.VerifyToken)]
    public async Task Verification_WithAnythingElse_Returns403(string mode, string token)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{Route}?hub.mode={mode}&hub.verify_token={token}&hub.challenge=1158201444");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("1158201444", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Verification_WithoutAChallenge_Returns403()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{Route}?hub.mode=subscribe&hub.verify_token={WhatsAppWebhookApiFactory.VerifyToken}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- delivery reports ---------------------------------------------------------------------------

    [Fact]
    public async Task ASignedDeliveryReport_IsAccepted_AndLoggedWithAMaskedNumber()
    {
        var id = UniqueId();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            Route,
            Signed(Call($$"""{"statuses":[{"id":"{{id}}","status":"delivered","recipient_id":"919876543210"}]}""")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(WebhookLogs(), e => e.Message.Contains(id, StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("is delivered", entry.Message, StringComparison.Ordinal);
        Assert.Contains("**********10", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("919876543210", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailureReport_IsLoggedAsAWarning_WithMetasReason()
    {
        var id = UniqueId();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            Route,
            Signed(Call($$"""{"statuses":[{"id":"{{id}}","status":"failed","recipient_id":"919876543210","errors":[{"code":131026,"title":"Message undeliverable"}]}]}""")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(WebhookLogs(), e => e.Message.Contains(id, StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("131026", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Message undeliverable", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("919876543210", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInboundMessage_IsAcknowledged_WithoutItsContentBeingLogged()
    {
        var id = UniqueId();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            Route,
            Signed(Call($$"""{"messages":[{"from":"919876543210","id":"{{id}}","text":{"body":"my private words"},"type":"text"}]}""")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(WebhookLogs(), e => e.Message.Contains(id, StringComparison.Ordinal));
        Assert.DoesNotContain("my private words", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("919876543210", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACallOfAnotherKind_IsAcceptedAndIgnored()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            Route, Signed(Call("""{"event":"APPROVED","message_template_name":"exam_login_code"}""")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- who may call -------------------------------------------------------------------------------

    [Fact]
    public async Task ACallSignedWithAnotherSecret_IsRefused_AndItsReportIsNotLogged()
    {
        var id = UniqueId();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            Route,
            Signed(Call($$"""{"statuses":[{"id":"{{id}}","status":"delivered","recipient_id":"919876543210"}]}"""), secret: "someone-elses-secret"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(WebhookLogs(), e => e.Message.Contains(id, StringComparison.Ordinal));
        Assert.Contains(WebhookLogs(), e => e.Level == LogLevel.Warning && e.Message.Contains("refused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUnsignedCall_IsRefused()
    {
        var id = UniqueId();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            Route,
            Signed(Call($$"""{"statuses":[{"id":"{{id}}","status":"delivered"}]}"""), secret: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(WebhookLogs(), e => e.Message.Contains(id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABodyChangedAfterItWasSigned_IsRefused()
    {
        var id = UniqueId();
        var signedBody = Call($$"""{"statuses":[{"id":"{{id}}","status":"delivered"}]}""");
        using var content = new StringContent(signedBody.Replace("delivered", "failed", StringComparison.Ordinal), Encoding.UTF8, "application/json");
        content.Headers.TryAddWithoutValidation(
            WhatsAppWebhookSignature.HeaderName,
            WhatsAppWebhookSignature.Compute(Encoding.UTF8.GetBytes(signedBody), WhatsAppWebhookApiFactory.AppSecret));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(Route, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ASignedBodyThatIsNotJson_Returns400()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync(Route, Signed("this is not json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ABodyOverTheLimit_IsRefusedBeforeItIsRead()
    {
        using var client = factory.CreateClient();
        using var content = new ByteArrayContent(new byte[(1024 * 1024) + 1]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync(Route, content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public void TheRoutes_AreOpenToAnyCaller_BecauseTheyProveTheCallerThemselves()
    {
        var routes = EndpointAuthorizationInspector.ListRoutes(factory.Services)
            .Where(r => r.Pattern == Route)
            .ToList();

        Assert.Equal(["GET", "POST"], routes.Select(r => r.Method).Order());
        Assert.All(routes, r => Assert.False(r.RequiresAuthorization));
    }
}

/// <summary>Until the app secret and verify token are set, the webhook is not there: nothing can be proven, so nothing is answered.</summary>
public sealed class WhatsAppWebhookNotConfiguredTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Verification_Returns404()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=anything&hub.challenge=1158201444");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AReport_Returns404_EvenIfItIsSignedWithAnyGuess()
    {
        const string body = """{"entry":[]}""";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        content.Headers.TryAddWithoutValidation(
            WhatsAppWebhookSignature.HeaderName, WhatsAppWebhookSignature.Compute(Encoding.UTF8.GetBytes(body), string.Empty));
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/v1/webhooks/whatsapp", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
