using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// An API that sends invitations on WhatsApp (a template is named and the platform's WhatsApp settings are filled in), with the
/// messages it sends captured. No mail server is configured, as in a deployment that has only WhatsApp working.
/// </summary>
public class WhatsAppInviteApiFactory : ApiFactory
{
    /// <summary>The template messages handed to WhatsApp so far.</summary>
    public List<WhatsAppTemplateMessage> Sent { get; } = [];

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration =>
        new Dictionary<string, string?>(base.AdditionalConfiguration)
        {
            ["WhatsApp:Enabled"] = "true",
            ["Invite:WhatsApp:TemplateName"] = "exam_invitation",
            ["WhatsApp:AccessToken"] = "integration-test-token",
            ["WhatsApp:PhoneNumberId"] = "1234567890",
        };

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IWhatsAppSender>(new RecordingSender(Sent)));
    }

    private sealed class RecordingSender(List<WhatsAppTemplateMessage> sent) : IWhatsAppSender
    {
        public Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken)
        {
            lock (sent) sent.Add(message);
            return Task.FromResult(new WhatsAppSendResult(true, "wamid.test"));
        }
    }
}

/// <summary>
/// When an invitation is created for an address that belongs to an account with a phone number, the exam code goes to that phone on
/// WhatsApp as well (FR-14, FR-39).
/// </summary>
public sealed class InviteWhatsAppDeliveryTests(WhatsAppInviteApiFactory factory) : IClassFixture<WhatsAppInviteApiFactory>
{
    private async Task<(HttpClient Admin, Guid ExamId)> PublishedExamAsync(string name)
    {
        var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Q?", "A", "B");
        return (admin, await CreateExamAsync(admin, name, [question], startsIn: TimeSpan.FromHours(-1)));
    }

    private WhatsAppTemplateMessage SentTo(string number)
    {
        lock (factory.Sent) return Assert.Single(factory.Sent, m => m.To == number);
    }

    [Fact]
    public async Task Create_ForAnAccountWithAPhone_SendsTheExamCodeOnWhatsApp_AndTheCodeWorks()
    {
        var (admin, examId) = await PublishedExamAsync("WhatsApp Invited Exam");
        using var adminClient = admin;
        var email = UniqueEmail();
        var (candidate, _) = await factory.CandidateClientAsync(email, phoneNumber: "98765 43211");
        using var candidateClient = candidate;

        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId, email });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invite = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(invite.GetProperty("whatsAppSent").GetBoolean());
        Assert.False(invite.GetProperty("emailSent").GetBoolean());

        // The message carried the code and the link, so the inviter has nothing to pass on.
        Assert.Equal(JsonValueKind.Null, invite.GetProperty("inviteLink").ValueKind);

        var message = SentTo("919876543211");
        Assert.Equal("exam_invitation", message.TemplateName);
        Assert.Equal("en", message.LanguageCode);
        Assert.Equal("WhatsApp Invited Exam", message.BodyParameters[0]);
        Assert.Matches("^[A-Z0-9]{8}$", message.BodyParameters[1]);
        Assert.EndsWith(message.BodyParameters[1], message.BodyParameters[2]);
        Assert.Matches(@"^\d{1,2} [A-Z][a-z]{2} \d{4} \d{2}:\d{2} UTC$", message.BodyParameters[3]);

        // The code in the message is the one that works: the invited candidate enters it on the invitation page.
        var accepted = await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = message.BodyParameters[1] });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task Create_ForAnAddressWithNoAccount_SendsNothingOnWhatsApp_AndHandsTheLinkBack()
    {
        var (admin, examId) = await PublishedExamAsync("Nobody Yet Exam");
        using var adminClient = admin;
        var before = factory.Sent.Count;

        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId, email = UniqueEmail() });

        var invite = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(invite.GetProperty("whatsAppSent").GetBoolean());
        Assert.Equal(JsonValueKind.String, invite.GetProperty("inviteLink").ValueKind);
        Assert.Equal(before, factory.Sent.Count);
    }

    [Fact]
    public async Task Create_ForAnAccountWithoutAPhone_SendsNothingOnWhatsApp()
    {
        var (admin, examId) = await PublishedExamAsync("No Phone Exam");
        using var adminClient = admin;
        var email = UniqueEmail();
        var (candidate, _) = await factory.CandidateClientAsync(email);
        using var candidateClient = candidate;
        var before = factory.Sent.Count;

        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId, email });

        var invite = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(invite.GetProperty("whatsAppSent").GetBoolean());
        Assert.Equal(before, factory.Sent.Count);
    }

    [Fact]
    public async Task Create_ForAPhoneWhatsAppCannotReach_StillCreatesTheInviteAndHandsTheLinkBack()
    {
        var (admin, examId) = await PublishedExamAsync("Odd Number Exam");
        using var adminClient = admin;
        var email = UniqueEmail();
        var (candidate, _) = await factory.CandidateClientAsync(email, phoneNumber: "12345");
        using var candidateClient = candidate;
        var before = factory.Sent.Count;

        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId, email });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invite = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(invite.GetProperty("whatsAppSent").GetBoolean());
        Assert.Equal(JsonValueKind.String, invite.GetProperty("inviteLink").ValueKind);
        Assert.Equal(before, factory.Sent.Count);
    }
}

/// <summary>The same API, with a template named and the credentials in place, but the master switch off.</summary>
public sealed class WhatsAppInviteSwitchedOffApiFactory : WhatsAppInviteApiFactory
{
    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration =>
        new Dictionary<string, string?>(base.AdditionalConfiguration) { ["WhatsApp:Enabled"] = "false" };
}

/// <summary>With the master switch off, nothing goes to WhatsApp however much else is configured.</summary>
public sealed class InviteWhatsAppSwitchedOffTests(WhatsAppInviteSwitchedOffApiFactory factory)
    : IClassFixture<WhatsAppInviteSwitchedOffApiFactory>
{
    [Fact]
    public async Task Create_ForAnAccountWithAPhone_SendsNothingOnWhatsApp_WhileTheMasterSwitchIsOff()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Q?", "A", "B");
        var examId = await CreateExamAsync(admin, "Switched Off Exam", [question], startsIn: TimeSpan.FromHours(-1));
        var email = UniqueEmail();
        var (candidate, _) = await factory.CandidateClientAsync(email, phoneNumber: "98765 43213");
        using var candidateClient = candidate;

        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId, email });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invite = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(invite.GetProperty("whatsAppSent").GetBoolean());
        Assert.Equal(JsonValueKind.String, invite.GetProperty("inviteLink").ValueKind);
        lock (factory.Sent) Assert.Empty(factory.Sent);
    }
}

/// <summary>Without a template named, nothing about an invitation goes to WhatsApp and no phone number is looked up for it.</summary>
public sealed class InviteWhatsAppDisabledTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Create_ForAnAccountWithAPhone_SendsNothingOnWhatsApp_WhenNoTemplateIsConfigured()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Q?", "A", "B");
        var examId = await CreateExamAsync(admin, "Quiet Exam", [question], startsIn: TimeSpan.FromHours(-1));
        var email = UniqueEmail();
        var (candidate, _) = await factory.CandidateClientAsync(email, phoneNumber: "98765 43212");
        using var candidateClient = candidate;

        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId, email });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invite = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(invite.GetProperty("whatsAppSent").GetBoolean());
        Assert.Equal(JsonValueKind.String, invite.GetProperty("inviteLink").ValueKind);
    }
}
