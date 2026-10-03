using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ExamPlatform.IntegrationTests;

/// <summary>A just-enough SMTP server: accepts one message per connection and records it.</summary>
internal sealed class SmtpSink : IAsyncDisposable
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
        catch (InvalidOperationException)
        {
            // Disposed before the loop reached its first accept, so the listener was already stopped: also nothing to serve.
            // A test that never connects (a notifier with no mail server configured) finishes fast enough to hit this.
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
