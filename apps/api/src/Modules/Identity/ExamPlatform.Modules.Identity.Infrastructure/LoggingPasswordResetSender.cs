using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Privacy;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// DEV ONLY. Logs the password-reset link instead of sending it, as <see cref="LoggingOtpSender"/> does for sign-in codes. The Identity
/// module uses it only when <c>Identity:OtpDelivery:Provider</c> is <c>DevelopmentLog</c>, which startup validation allows only in the
/// Development environment (NFR-6).
/// </summary>
public sealed class LoggingPasswordResetSender(
    IPasswordResetLinkBuilder links, ILogger<LoggingPasswordResetSender> logger) : IPasswordResetSender
{
    /// <inheritdoc />
    public Task SendAsync(string email, string code, TimeSpan validity, CancellationToken cancellationToken)
    {
        // The link is still logged, because resetting a password during local development depends on reading it here; the destination
        // is masked, so the log never holds a raw e-mail address. Warning, so it stands out.
        logger.LogWarning(
            "[DEV ONLY - never enabled outside Development] password-reset link for {MaskedDestination}, valid for {ValidityMinutes} minutes: {Link}",
            ContactMasker.Mask(OtpChannel.Email, email),
            validity.TotalMinutes,
            links.Build(code));
        return Task.CompletedTask;
    }
}
