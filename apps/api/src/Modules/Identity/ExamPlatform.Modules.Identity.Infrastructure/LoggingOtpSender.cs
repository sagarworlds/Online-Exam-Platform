using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Privacy;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// DEV ONLY. Logs the OTP code instead of sending it, since no real email/SMS
/// provider credentials exist in this environment. The Identity module only uses it
/// when <c>Identity:OtpDelivery:Provider</c> is <c>DevelopmentLog</c>, which startup
/// validation allows only in the Development environment (NFR-6). Replace with an
/// adapter onto the future Notifications module's email/SMS channels (FR-39) — nothing
/// outside this class needs to change to swap it, since callers only depend on
/// <see cref="IOtpSender"/> (DIP).
/// </summary>
public sealed class LoggingOtpSender(ILogger<LoggingOtpSender> logger) : IOtpSender
{
    /// <inheritdoc />
    public Task SendAsync(OtpChannel channel, string destination, string code, CancellationToken cancellationToken)
    {
        // The code (or reset link) is still logged, because signing in during local
        // development depends on reading it here; the destination is masked, so the log
        // never holds a raw email address or phone number. Warning, so it stands out.
        logger.LogWarning(
            "[DEV ONLY - never enabled outside Development] {Channel} code for {MaskedDestination}: {Code}",
            channel,
            ContactMasker.Mask(channel, destination),
            code);
        return Task.CompletedTask;
    }
}
