using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Invite.Application.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>An API whose invitation e-mail is captured and reported as delivered, as with a working mail server.</summary>
public sealed class MailDeliveringApiFactory : ApiFactory
{
    /// <summary>The invitations "sent" so far.</summary>
    public List<InviteEmail> Sent { get; } = [];

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IInviteNotifier>(new RecordingNotifier(Sent)));
    }

    private sealed class RecordingNotifier(List<InviteEmail> sent) : IInviteNotifier
    {
        public Task<bool> SendAsync(InviteEmail email, CancellationToken cancellationToken)
        {
            lock (sent) sent.Add(email);
            return Task.FromResult(true);
        }
    }
}

/// <summary>What the API does once the invitation e-mail really went out (FR-14, FR-39).</summary>
public sealed class InviteEmailDeliveryTests(MailDeliveringApiFactory factory) : IClassFixture<MailDeliveringApiFactory>
{
    [Fact]
    public async Task Create_WhenTheMailIsDelivered_EmailsTheLinkAndDoesNotHandItBackToTheInviter()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Q?", "A", "B");
        var examId = await CreateExamAsync(admin, "Mailed Exam", [question], startsIn: TimeSpan.FromHours(-1));
        var email = UniqueEmail();

        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId, email });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invite = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(invite.GetProperty("emailSent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, invite.GetProperty("inviteLink").ValueKind);

        InviteEmail sent;
        lock (factory.Sent) sent = Assert.Single(factory.Sent, m => m.To == email);
        Assert.Equal("Mailed Exam", sent.ExamName);
        Assert.Matches(@"/invite\?code=[A-Z0-9]{8}$", sent.Link);

        // The link in the mail is the one that works: the invited candidate accepts with it.
        var (candidate, _) = await factory.CandidateClientAsync(email);
        var accepted = await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = CodeFromLink(sent.Link) });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }
}
