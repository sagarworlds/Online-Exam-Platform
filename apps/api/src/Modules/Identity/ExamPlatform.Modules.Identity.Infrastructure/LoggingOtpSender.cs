using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// DEV ONLY. Logs the OTP code instead of sending it, since no real email/SMS
/// provider credentials exist in this environment. Replace with an adapter onto
/// the future Notifications module's email/SMS channels (FR-39) before any
/// non-development deployment — nothing outside this class needs to change to
/// swap it, since callers only depend on <see cref="IOtpSender"/> (DIP).
/// </summary>
public sealed class LoggingOtpSender(ILogger<LoggingOtpSender> logger) : IOtpSender
{
    /// <inheritdoc />
    public Task SendAsync(OtpChannel channel, string destination, string code, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[DEV OTP SENDER] {Channel} code for {Destination}: {Code}", channel, destination, code);
        return Task.CompletedTask;
    }
}
