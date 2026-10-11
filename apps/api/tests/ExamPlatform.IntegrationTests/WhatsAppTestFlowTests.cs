using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The Host with WhatsApp fully configured and a sender the test controls, so an administrator's test can be driven through every outcome
/// without calling Meta.
/// </summary>
public sealed class WhatsAppTestApiFactory : ApiFactory
{
    /// <summary>The app secret the webhook's signatures are checked against.</summary>
    public const string AppSecret = "integration-test-app-secret";

    /// <summary>Stands in for Meta: records what it was asked to send, and answers as the test says.</summary>
    public ScriptedWhatsAppSender Sender { get; } = new();

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration =>
        new Dictionary<string, string?>(base.AdditionalConfiguration)
        {
            ["WhatsApp:Enabled"] = "true",
            ["WhatsApp:AccessToken"] = "EAAG-integration-secret-token",
            ["WhatsApp:PhoneNumberId"] = "1234567890",
            ["WhatsApp:OtpTemplateName"] = "exam_login_code",
            ["WhatsApp:AppSecret"] = AppSecret,
            ["WhatsApp:WebhookVerifyToken"] = "integration-test-verify-token",
            ["Identity:OtpDelivery:PhoneProvider"] = "WhatsApp",
        };

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IWhatsAppSender>(Sender));
    }

    /// <summary>A sender that answers each kind of message with whatever the test set.</summary>
    public sealed class ScriptedWhatsAppSender : IWhatsAppSender
    {
        private readonly object _gate = new();

        /// <summary>The text messages it was asked to send, oldest first.</summary>
        public List<WhatsAppTextMessage> Texts { get; } = [];

        /// <summary>The template messages it was asked to send, oldest first.</summary>
        public List<WhatsAppTemplateMessage> Templates { get; } = [];

        /// <summary>How it answers the next text message.</summary>
        public Func<WhatsAppSendResult> OnText { get; set; } = () => new WhatsAppSendResult(true, $"wamid.{Guid.NewGuid():N}");

        /// <summary>How it answers the next template message.</summary>
        public Func<WhatsAppSendResult> OnTemplate { get; set; } = () => new WhatsAppSendResult(true, $"wamid.{Guid.NewGuid():N}");

        /// <summary>Forgets what was sent and answers "accepted" again.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                Texts.Clear();
                Templates.Clear();
                OnText = () => new WhatsAppSendResult(true, $"wamid.{Guid.NewGuid():N}");
                OnTemplate = () => new WhatsAppSendResult(true, $"wamid.{Guid.NewGuid():N}");
            }
        }

        /// <inheritdoc />
        public Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Templates.Add(message);
                return Task.FromResult(OnTemplate());
            }
        }

        /// <inheritdoc />
        public Task<WhatsAppSendResult> SendTextAsync(WhatsAppTextMessage message, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Texts.Add(message);
                return Task.FromResult(OnText());
            }
        }
    }
}

/// <summary>
/// The administrator's WhatsApp test over real HTTP: who may use it, what it says is configured, a message sent through the connection and
/// the real reason when it cannot be, and what Meta later reports. It sends real messages to real numbers, so it is SuperAdmin-only and
/// audited without recording the words.
/// </summary>
public sealed class WhatsAppTestFlowTests : IClassFixture<WhatsAppTestApiFactory>
{
    private const string Status = "/v1/admin/whatsapp/status";
    private const string Messages = "/v1/admin/whatsapp/messages";

    private readonly WhatsAppTestApiFactory factory;

    // A new instance for every test, so each starts with a sender that accepts everything and has been asked nothing.
    public WhatsAppTestFlowTests(WhatsAppTestApiFactory factory)
    {
        this.factory = factory;
        factory.Sender.Reset();
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Test data: a phone number that only has to be unique per call. It is not a secret or a token.")]
    private static string UniqueNumber() => "9" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private Task<HttpResponseMessage> SendAsync(HttpClient client, object body) => client.PostAsJsonAsync(Messages, body);

    private async Task<HttpClient> ExamAdminAsync()
    {
        var client = factory.CreateClient();
        var user = await factory.SignInAsAsync(RbacCatalog.RoleNames.ExamAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return client;
    }

    // A delivery report as Meta sends it. Built by replacement, not interpolation: a raw string cannot hold the run of closing braces.
    private static string StatusReport(string id, string status, string recipient, string errors = "") =>
        """{"object":"whatsapp_business_account","entry":[{"id":"WABA","changes":[{"field":"messages","value":{"messaging_product":"whatsapp","statuses":[{"id":"@ID","status":"@STATUS","timestamp":"1","recipient_id":"@TO"@ERRORS}]}}]}]}"""
            .Replace("@ID", id, StringComparison.Ordinal)
            .Replace("@STATUS", status, StringComparison.Ordinal)
            .Replace("@TO", recipient, StringComparison.Ordinal)
            .Replace("@ERRORS", errors, StringComparison.Ordinal);

    private static HttpContent SignedWebhook(string body)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        content.Headers.TryAddWithoutValidation(
            WhatsAppWebhookSignature.HeaderName,
            WhatsAppWebhookSignature.Compute(Encoding.UTF8.GetBytes(body), WhatsAppTestApiFactory.AppSecret));
        return content;
    }

    // ---- who may ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_IsAskedToSignIn_OnEveryRoute()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Status)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, new { phoneNumber = "9876543210", mode = "Text", message = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Messages}/wamid.x")).StatusCode);
    }

    [Fact]
    public async Task OnlyASuperAdmin_MayUseIt_NotEvenTheAdministratorWhoRunsExams()
    {
        using var examAdmin = await ExamAdminAsync();
        var (candidate, _) = await factory.CandidateClientAsync();
        using var _c = candidate;

        foreach (var client in new[] { examAdmin, candidate })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Status)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, new { phoneNumber = "9876543210", mode = "Text", message = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Messages}/wamid.x")).StatusCode);
        }

        Assert.Empty(factory.Sender.Texts.Where(t => t.Body == "x"));
    }

    // ---- the setup check -----------------------------------------------------------------------------

    [Fact]
    public async Task TheStatus_ShowsWhatIsConfigured_AndNoSecret()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.GetAsync(Status);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        // The token, the app secret and the verify token are never returned, however they are named.
        Assert.DoesNotContain("EAAG-integration-secret-token", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(WhatsAppTestApiFactory.AppSecret, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("integration-test-verify-token", raw, StringComparison.Ordinal);

        var status = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.True(status.GetProperty("canSendMessages").GetBoolean());
        Assert.True(status.GetProperty("canSendTemplate").GetBoolean());
        Assert.True(status.GetProperty("canTrackDelivery").GetBoolean());
        Assert.True(status.GetProperty("signInCodesUseWhatsApp").GetBoolean());
        Assert.False(status.GetProperty("inviteCodesUseWhatsApp").GetBoolean());
        Assert.Empty(status.GetProperty("problems").EnumerateArray());

        var settings = status.GetProperty("settings").EnumerateArray().ToDictionary(s => s.GetProperty("setting").GetString()!);
        Assert.True(settings["WhatsApp__AccessToken"].GetProperty("isSet").GetBoolean());
        Assert.Equal(JsonValueKind.Null, settings["WhatsApp__AccessToken"].GetProperty("value").ValueKind);
        Assert.Equal("1234567890", settings["WhatsApp__PhoneNumberId"].GetProperty("value").GetString());
        Assert.Equal("exam_login_code", settings["WhatsApp__OtpTemplateName"].GetProperty("value").GetString());
        Assert.False(settings["Invite__WhatsApp__TemplateName"].GetProperty("isSet").GetBoolean());
    }

    [Fact]
    public async Task OnAHostWhereWhatsAppIsNotConfigured_TheStatusSaysEverythingThatIsMissing()
    {
        using var signedIn = await factory.AdminClientAsync();
        // The same host and database with WhatsApp left as a new installation has it: everything blank.
        using var bare = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["WhatsApp:Enabled"] = "",
                ["WhatsApp:AccessToken"] = "",
                ["WhatsApp:PhoneNumberId"] = "",
                ["WhatsApp:OtpTemplateName"] = "",
                ["WhatsApp:AppSecret"] = "",
                ["WhatsApp:WebhookVerifyToken"] = "",
                ["Identity:OtpDelivery:PhoneProvider"] = "",
            })));
        using var admin = bare.CreateClient();
        admin.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;

        var status = await JsonAsync((await admin.GetAsync(Status)).EnsureSuccessStatusCode());

        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.False(status.GetProperty("canSendMessages").GetBoolean());
        var problems = status.GetProperty("problems").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains(problems, p => p!.Contains("switched off", StringComparison.Ordinal));
        Assert.Contains(problems, p => p!.Contains("WhatsApp__AccessToken", StringComparison.Ordinal));
        Assert.Contains(problems, p => p!.Contains("WhatsApp__PhoneNumberId", StringComparison.Ordinal));
    }

    // ---- sending -------------------------------------------------------------------------------------

    [Fact]
    public async Task ATextMessage_IsSentThroughTheRealSender_WithTheDefaultCountryCodeAdded()
    {
        using var admin = await factory.AdminClientAsync();
        var number = UniqueNumber();

        var result = await JsonAsync((await SendAsync(admin, new { phoneNumber = number, mode = "Text", message = "  Hello from admin  " })).EnsureSuccessStatusCode());

        Assert.True(result.GetProperty("sent").GetBoolean());
        Assert.StartsWith("wamid.", result.GetProperty("messageId").GetString(), StringComparison.Ordinal);
        Assert.Equal("Text", result.GetProperty("mode").GetString());
        Assert.True(result.GetProperty("deliveryTracking").GetBoolean());
        Assert.Contains("not delivery", result.GetProperty("note").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("failure").ValueKind);
        // Masked in the answer; whole, with the platform's country code, to Meta.
        Assert.EndsWith(number[^2..], result.GetProperty("to").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(number, result.GetProperty("to").GetString()!, StringComparison.Ordinal);
        var sent = Assert.Single(factory.Sender.Texts, t => t.To == "91" + number);
        Assert.Equal("Hello from admin", sent.Body);
    }

    [Fact]
    public async Task TheSignInTemplate_IsSentAsSignInSendsIt_WithATestCodeThatOpensNothing()
    {
        using var admin = await factory.AdminClientAsync();
        var number = UniqueNumber();

        var result = await JsonAsync((await SendAsync(admin, new { phoneNumber = number, mode = "SignInTemplate" })).EnsureSuccessStatusCode());

        Assert.True(result.GetProperty("sent").GetBoolean());
        Assert.Contains("cannot be used to sign in", result.GetProperty("note").GetString(), StringComparison.Ordinal);
        var sent = Assert.Single(factory.Sender.Templates, t => t.To == "91" + number);
        Assert.Equal("exam_login_code", sent.TemplateName);
        Assert.Equal("en", sent.LanguageCode);
        // The same shape sign-in uses: the code in the body and in the copy-code button.
        var code = Assert.Single(sent.BodyParameters);
        Assert.Equal(6, code.Length);
        Assert.Equal(code, sent.UrlButtonParameter);
    }

    [Fact]
    public async Task WhenTheMessageIsRefused_TheAnswerSaysWhy_AndTheRequestStillSucceeds()
    {
        using var admin = await factory.AdminClientAsync();
        factory.Sender.OnText = () => new WhatsAppSendResult(
            false, Failure: WhatsAppErrorGuide.Explain(400, 131030, "(#131030) Recipient phone number not in allowed list"));

        var response = await SendAsync(admin, new { phoneNumber = UniqueNumber(), mode = "Text", message = "Hello" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await JsonAsync(response);
        Assert.False(result.GetProperty("sent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("messageId").ValueKind);
        var failure = result.GetProperty("failure");
        Assert.Equal("RecipientNotAllowed", failure.GetProperty("kind").GetString());
        Assert.Contains("Manage phone number list", failure.GetProperty("explanation").GetString(), StringComparison.Ordinal);
        Assert.Equal(131030, failure.GetProperty("metaCode").GetInt32());
        Assert.Equal(400, failure.GetProperty("httpStatus").GetInt32());
        Assert.Contains("not in allowed list", failure.GetProperty("metaMessage").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANumberThatIsNotANumber_IsReportedAsOne_WithoutCallingWhatsApp()
    {
        using var admin = await factory.AdminClientAsync();
        var before = factory.Sender.Texts.Count;

        var result = await JsonAsync((await SendAsync(admin, new { phoneNumber = "call me maybe", mode = "Text", message = "Hello" })).EnsureSuccessStatusCode());

        Assert.False(result.GetProperty("sent").GetBoolean());
        Assert.Equal("InvalidNumber", result.GetProperty("failure").GetProperty("kind").GetString());
        Assert.Equal(before, factory.Sender.Texts.Count);
    }

    [Theory]
    [InlineData(null, "Text", "Hello")]
    [InlineData("", "Text", "Hello")]
    [InlineData("   ", "Text", "Hello")]
    [InlineData("9876543210", "Text", null)]
    [InlineData("9876543210", "Text", "  ")]
    [InlineData("9876543210", "Carrier-Pigeon", "Hello")]
    [InlineData("9876543210", null, "Hello")]
    public async Task ARequestThatCannotBeSent_IsRefusedAsMalformed_BeforeAnythingIsSent(string? number, string? mode, string? message)
    {
        using var admin = await factory.AdminClientAsync();
        var before = (factory.Sender.Texts.Count, factory.Sender.Templates.Count);

        var response = await SendAsync(admin, new { phoneNumber = number, mode, message });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_whatsapp_message");
        Assert.Equal(before, (factory.Sender.Texts.Count, factory.Sender.Templates.Count));
    }

    [Fact]
    public async Task AMessageOfTheLongestLength_IsSent_AndOneCharacterMoreIsRefused()
    {
        using var admin = await factory.AdminClientAsync();

        (await SendAsync(admin, new { phoneNumber = UniqueNumber(), mode = "Text", message = new string('a', 1000) })).EnsureSuccessStatusCode();
        await AssertProblemAsync(
            await SendAsync(admin, new { phoneNumber = UniqueNumber(), mode = "Text", message = new string('a', 1001) }),
            HttpStatusCode.BadRequest,
            "invalid_whatsapp_message");
    }

    [Fact]
    public async Task EverySend_IsAudited_WithoutTheWordsOrTheWholeNumber()
    {
        using var admin = await factory.AdminClientAsync();
        var number = UniqueNumber();
        var sent = await JsonAsync((await SendAsync(admin, new { phoneNumber = number, mode = "Text", message = "Meet me at the station, Priya" })).EnsureSuccessStatusCode());
        var id = sent.GetProperty("messageId").GetString();

        var raw = await (await admin.GetAsync("/v1/admin/audit-logs?page=1&pageSize=50")).EnsureSuccessStatusCode().Content.ReadAsStringAsync();

        var entry = JsonSerializer.Deserialize<JsonElement>(raw).EnumerateArray()
            .Single(e => e.GetProperty("action").GetString() == "Identity.WhatsAppTestSent" && e.GetProperty("entityId").GetString() == id);
        Assert.Equal("SuperAdmin", entry.GetProperty("actorRole").GetString());
        var metadata = entry.GetProperty("metadata");
        Assert.Equal("Text", metadata.GetProperty("mode").GetString());
        Assert.Equal("Sent", metadata.GetProperty("outcome").GetString());
        Assert.Equal("29", metadata.GetProperty("length").GetString());
        Assert.DoesNotContain("station", entry.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(number, entry.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedSend_IsAuditedWithTheKindOfFailure()
    {
        using var admin = await factory.AdminClientAsync();
        factory.Sender.OnTemplate = () => new WhatsAppSendResult(false, Failure: WhatsAppErrorGuide.Explain(404, 132001, null, "exam_login_code", "en"));
        var marker = UniqueNumber();
        (await SendAsync(admin, new { phoneNumber = marker, mode = "SignInTemplate" })).EnsureSuccessStatusCode();

        var raw = await (await admin.GetAsync("/v1/admin/audit-logs?page=1&pageSize=50")).EnsureSuccessStatusCode().Content.ReadAsStringAsync();

        var masked = JsonSerializer.Deserialize<JsonElement>(raw).EnumerateArray()
            .Where(e => e.GetProperty("action").GetString() == "Identity.WhatsAppTestSent"
                        && e.GetProperty("metadata").GetProperty("outcome").GetString() == "TemplateNotFound")
            .ToList();
        Assert.NotEmpty(masked);
    }

    // ---- what Meta reports later -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeMetaHasReported_TheDeliveryIsNotReported()
    {
        using var admin = await factory.AdminClientAsync();

        var delivery = await JsonAsync((await admin.GetAsync($"{Messages}/wamid.never-heard-of")).EnsureSuccessStatusCode());

        Assert.Equal("wamid.never-heard-of", delivery.GetProperty("messageId").GetString());
        Assert.Equal("NotReported", delivery.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, delivery.GetProperty("failure").ValueKind);
    }

    [Fact]
    public async Task AFailedDeliveryReport_ShowsUpAsTheRealReason()
    {
        using var admin = await factory.AdminClientAsync();
        var number = UniqueNumber();
        var sent = await JsonAsync((await SendAsync(admin, new { phoneNumber = number, mode = "Text", message = "Hello" })).EnsureSuccessStatusCode());
        var id = sent.GetProperty("messageId").GetString();

        // Meta accepted the message and, two seconds later, says why it did not arrive: the 24-hour window.
        using var meta = factory.CreateClient();
        var report = StatusReport(
            id!,
            "failed",
            "91" + number,
            ""","errors":[{"code":131047,"title":"Re-engagement message","message":"Re-engagement message","error_data":{"details":"More than 24 hours have passed since the customer last replied to this number."}}]""");
        Assert.Equal(HttpStatusCode.OK, (await meta.PostAsync("/v1/webhooks/whatsapp", SignedWebhook(report))).StatusCode);

        var delivery = await JsonAsync((await admin.GetAsync($"{Messages}/{id}")).EnsureSuccessStatusCode());

        Assert.Equal("failed", delivery.GetProperty("status").GetString());
        var failure = delivery.GetProperty("failure");
        Assert.Equal("ReEngagementRequired", failure.GetProperty("kind").GetString());
        Assert.Contains("24 hours", failure.GetProperty("explanation").GetString(), StringComparison.Ordinal);
        Assert.Contains("More than 24 hours have passed", failure.GetProperty("metaMessage").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(number, delivery.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProgressReports_AreKept_InOrder()
    {
        using var admin = await factory.AdminClientAsync();
        var id = $"wamid.{Guid.NewGuid():N}";
        using var meta = factory.CreateClient();

        foreach (var state in new[] { "sent", "delivered", "sent" })
        {
            var report = StatusReport(id, state, "919876543210");
            Assert.Equal(HttpStatusCode.OK, (await meta.PostAsync("/v1/webhooks/whatsapp", SignedWebhook(report))).StatusCode);
        }

        var delivery = await JsonAsync((await admin.GetAsync($"{Messages}/{id}")).EnsureSuccessStatusCode());

        // A late "sent" does not undo "delivered".
        Assert.Equal("delivered", delivery.GetProperty("status").GetString());
        Assert.EndsWith("10", delivery.GetProperty("recipient").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReportThatIsNotSigned_IsNotBelieved()
    {
        using var admin = await factory.AdminClientAsync();
        var id = $"wamid.{Guid.NewGuid():N}";
        using var meta = factory.CreateClient();
        var body = StatusReport(id, "read", "919876543210");

        var response = await meta.PostAsync("/v1/webhooks/whatsapp", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("NotReported", (await JsonAsync(await admin.GetAsync($"{Messages}/{id}"))).GetProperty("status").GetString());
    }
}
