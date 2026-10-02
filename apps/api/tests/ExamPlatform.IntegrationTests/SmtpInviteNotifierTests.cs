using System.Net;
using System.Net.Sockets;
using System.Text;
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

    /// <summary>A just-enough SMTP server: accepts one message per connection and records it.</summary>
    private sealed class SmtpSink : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource<(string Envelope, string Data)> _message = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _refuseRecipients;
        private readonly Task _loop;
        private int _connections;

        public SmtpSink(bool refuseRecipients = false)
        {
            _refuseRecipients = refuseRecipients;
            _listener.Start();
            _loop = Task.Run(AcceptLoopAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int Connections => Volatile.Read(ref _connections);

        public static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public async Task<(string Envelope, string Data)> WaitForMessageAsync() =>
            await _message.Task.WaitAsync(TimeSpan.FromSeconds(10));

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    Interlocked.Increment(ref _connections);
                    _ = Task.Run(() => ServeAsync(client));
                }
            }
            catch (OperationCanceledException)
            {
                // Disposed while waiting for a client: nothing more to serve.
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using var _ = client;
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            var envelope = new StringBuilder();

            await writer.WriteLineAsync("220 sink ready");
            while (await reader.ReadLineAsync() is { } line)
            {
                var verb = line.Split(' ')[0].ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO" or "HELO":
                        await writer.WriteLineAsync("250 sink");
                        break;
                    case "MAIL":
                        envelope.AppendLine(line);
                        await writer.WriteLineAsync("250 ok");
                        break;
                    case "RCPT":
                        envelope.AppendLine(line);
                        await writer.WriteLineAsync(_refuseRecipients ? "550 no such user" : "250 ok");
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 end with <CRLF>.<CRLF>");
                        var data = new StringBuilder();
                        while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                            data.AppendLine(dataLine);
                        _message.TrySetResult((envelope.ToString(), data.ToString()));
                        await writer.WriteLineAsync("250 queued");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 bye");
                        return;
                    default:
                        await writer.WriteLineAsync("250 ok");
                        break;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _loop;
            }
            catch (ObjectDisposedException)
            {
                // The listener was already torn down.
            }
        }
    }
}
