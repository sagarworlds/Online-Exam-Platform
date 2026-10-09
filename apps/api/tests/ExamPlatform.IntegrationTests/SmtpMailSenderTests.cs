using ExamPlatform.SharedKernel.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The platform's mail sender against a small SMTP server running in this process, so what is checked is the real protocol exchange:
/// a message is delivered to the right recipient, and a server that refuses it, or that is not there at all, turns into "not sent"
/// rather than an exception that would lose what the message was about.
/// </summary>
public sealed class SmtpMailSenderTests
{
    private static readonly OutgoingMail Mail = new(
        "candidate@example.com",
        "A subject",
        // Several lines, as every real message is: how the library encodes a body depends on its lines.
        "A body with a secret link:\r\nhttps://app.example/invite?code=AB12CD34\r\n\r\nIt works once.");

    private static SmtpMailSender SenderFor(int port, bool configured = true) =>
        new(Options.Create(new SmtpOptions
        {
            Host = configured ? "127.0.0.1" : null,
            Port = port,
            EnableSsl = false,
            From = "exams@examplatform.test",
        }), NullLogger<SmtpMailSender>.Instance);

    [Fact]
    public async Task Send_DeliversTheMessageToTheRecipient_FromTheConfiguredSender()
    {
        await using var server = new SmtpSink();

        var sent = await SenderFor(server.Port).SendAsync(Mail, CancellationToken.None);

        Assert.True(sent);
        var message = await server.WaitForMessageAsync();
        Assert.Contains("RCPT TO:<candidate@example.com>", message.Envelope, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MAIL FROM:<exams@examplatform.test>", message.Envelope, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Subject: A subject", message.Data);
        Assert.Contains("AB12CD34", message.Data);
    }

    [Fact]
    public async Task Send_WithNoMailServerConfigured_SendsNothingAndSaysSo()
    {
        await using var server = new SmtpSink();

        var sent = await SenderFor(server.Port, configured: false).SendAsync(Mail, CancellationToken.None);

        Assert.False(sent);
        Assert.Equal(0, server.Connections);
    }

    [Fact]
    public async Task Send_WhenTheServerRefusesTheRecipient_ReportsNotSent_InsteadOfThrowing()
    {
        await using var server = new SmtpSink(refuseRecipients: true);

        Assert.False(await SenderFor(server.Port).SendAsync(Mail, CancellationToken.None));
    }

    [Fact]
    public async Task Send_WhenNothingIsListening_ReportsNotSent_InsteadOfThrowing()
    {
        Assert.False(await SenderFor(SmtpSink.FreePort()).SendAsync(Mail, CancellationToken.None));
    }

    [Fact]
    public async Task TheBodyAndSubject_AreNeverWrittenToTheLog()
    {
        var logger = new CapturingLogger<SmtpMailSender>();
        var sender = new SmtpMailSender(Options.Create(new SmtpOptions { Host = "127.0.0.1", Port = SmtpSink.FreePort(), EnableSsl = false }), logger);

        await sender.SendAsync(Mail, CancellationToken.None);

        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("AB12CD34") || m.Contains("A subject"));
    }

    private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null)
                Messages.Add(exception.ToString());
        }
    }
}
