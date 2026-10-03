using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Exercises the SMTP adapter against a small SMTP server running in this process, so what is checked is the real
/// protocol exchange: a message is delivered to the right recipient, and a server that refuses it, or that is not
/// there at all, turns into "not sent" rather than an exception that would lose the invite.
/// </summary>
public sealed class SmtpInviteNotifierTests
{
    private static readonly InviteEmail Email =
        new("candidate@example.com", "Maths Final", "https://app.example/invite?code=AB12CD34", new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc));

    private static SmtpInviteNotifier NotifierFor(int port, bool configured = true) =>
        new(Options.Create(new SmtpOptions
        {
            Host = configured ? "127.0.0.1" : null,
            Port = port,
            EnableSsl = false,
            From = "exams@examplatform.test",
        }), NullLogger<SmtpInviteNotifier>.Instance);

    [Fact]
    public async Task Send_DeliversTheInvitationToTheRecipient_WithTheLinkInTheBody()
    {
        await using var server = new SmtpSink();

        var sent = await NotifierFor(server.Port).SendAsync(Email, CancellationToken.None);

        Assert.True(sent);
        var message = await server.WaitForMessageAsync();
        Assert.Contains("RCPT TO:<candidate@example.com>", message.Envelope, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MAIL FROM:<exams@examplatform.test>", message.Envelope, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Subject: You are invited to take Maths Final", message.Data);
        Assert.Contains("AB12CD34", message.Data);
        Assert.Contains("invite", message.Data);
    }

    [Fact]
    public async Task Send_WithNoMailServerConfigured_SendsNothingAndSaysSo()
    {
        await using var server = new SmtpSink();

        var sent = await NotifierFor(server.Port, configured: false).SendAsync(Email, CancellationToken.None);

        Assert.False(sent);
        Assert.Equal(0, server.Connections);
    }

    [Fact]
    public async Task Send_WhenTheServerRefusesTheRecipient_ReportsNotSent_InsteadOfThrowing()
    {
        await using var server = new SmtpSink(refuseRecipients: true);

        var sent = await NotifierFor(server.Port).SendAsync(Email, CancellationToken.None);

        Assert.False(sent);
    }

    [Fact]
    public async Task Send_WhenNothingIsListening_ReportsNotSent_InsteadOfThrowing()
    {
        var closedPort = SmtpSink.FreePort();

        var sent = await NotifierFor(closedPort).SendAsync(Email, CancellationToken.None);

        Assert.False(sent);
    }
}
