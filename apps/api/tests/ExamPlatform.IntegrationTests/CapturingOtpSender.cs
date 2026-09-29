using System.Collections.Concurrent;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Test double replacing the real <see cref="IOtpSender"/> (the dev-only
/// <c>LoggingOtpSender</c>) so tests can read the code that would have been sent,
/// instead of scraping log output.
/// </summary>
public sealed class CapturingOtpSender : IOtpSender
{
    private readonly ConcurrentDictionary<string, string> _codesByDestination = new();

    /// <inheritdoc />
    public Task SendAsync(OtpChannel channel, string destination, string code, CancellationToken cancellationToken)
    {
        _codesByDestination[destination] = code;
        return Task.CompletedTask;
    }

    /// <summary>The most recent code sent to a destination.</summary>
    /// <param name="destination">The email address or phone number.</param>
    public string GetLastCode(string destination) => _codesByDestination[destination];
}
