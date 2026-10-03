using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The SMTP adapter that tells a candidate how their request for another attempt was answered, against a small SMTP server running
/// in this process: the right message reaches the right person, and a missing or refusing server is "not sent", never an exception.
/// </summary>
public sealed class SmtpAttemptRequestNotifierTests
{
    private static readonly AttemptRequestDecisionEmail Approved = new("candidate@example.com", "Maths Final", Approved: true, Note: null);
    private static readonly AttemptRequestDecisionEmail Declined = new("candidate@example.com", "Maths Final", Approved: false, Note: "Speak to your teacher");

    private static SmtpAttemptRequestNotifier NotifierFor(int port, bool configured = true) =>
        new(Options.Create(new SmtpOptions
        {
            Host = configured ? "127.0.0.1" : null,
            Port = port,
            EnableSsl = false,
            From = "exams@examplatform.test",
        }), NullLogger<SmtpAttemptRequestNotifier>.Instance);

    [Fact]
    public async Task AnApproval_IsDeliveredToTheCandidate_AndSaysTheyCanSitAgain()
    {
        await using var server = new SmtpSink();

        var sent = await NotifierFor(server.Port).SendDecisionAsync(Approved, CancellationToken.None);

        Assert.True(sent);
        var message = await server.WaitForMessageAsync();
        Assert.Contains("RCPT TO:<candidate@example.com>", message.Envelope, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MAIL FROM:<exams@examplatform.test>", message.Envelope, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Subject: You can take Maths Final again", message.Data);
        Assert.Contains("another attempt", message.Data);
    }

    [Fact]
    public async Task ADecline_CarriesTheAdministratorsReason()
    {
        await using var server = new SmtpSink();

        var sent = await NotifierFor(server.Port).SendDecisionAsync(Declined, CancellationToken.None);

        Assert.True(sent);
        var message = await server.WaitForMessageAsync();
        Assert.Contains("Subject: Your request for another attempt at Maths Final", message.Data);
        Assert.Contains("was declined", message.Data);
        Assert.Contains("Speak to your teacher", message.Data);
    }

    [Fact]
    public async Task ADeclineWithoutAReason_DoesNotInventOne()
    {
        await using var server = new SmtpSink();

        await NotifierFor(server.Port).SendDecisionAsync(Declined with { Note = null }, CancellationToken.None);

        var message = await server.WaitForMessageAsync();
        Assert.DoesNotContain("What they said", message.Data);
    }

    [Fact]
    public async Task WithNoMailServerConfigured_NothingIsSent_AndItIsSaid()
    {
        await using var server = new SmtpSink();

        var sent = await NotifierFor(server.Port, configured: false).SendDecisionAsync(Approved, CancellationToken.None);

        Assert.False(sent);
        Assert.Equal(0, server.Connections);
    }

    [Fact]
    public async Task WhenTheServerRefusesTheRecipient_ItReportsNotSent_InsteadOfThrowing()
    {
        await using var server = new SmtpSink(refuseRecipients: true);

        Assert.False(await NotifierFor(server.Port).SendDecisionAsync(Approved, CancellationToken.None));
    }

    [Fact]
    public async Task WhenNothingIsListening_ItReportsNotSent_InsteadOfThrowing()
    {
        Assert.False(await NotifierFor(SmtpSink.FreePort()).SendDecisionAsync(Approved, CancellationToken.None));
    }
}
