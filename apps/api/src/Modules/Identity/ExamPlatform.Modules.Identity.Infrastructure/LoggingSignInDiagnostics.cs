using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Privacy;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// DEV ONLY, the counterpart of <see cref="LoggingOtpSender"/>: where that one logs the code that was sent, this one logs why
/// no code was sent, so a developer is not left waiting at a verify screen. Used only when the OTP provider is the development
/// log, which startup validation allows only in the Development environment (NFR-6); the destination is masked, as in
/// <see cref="LoggingOtpSender"/>.
/// </summary>
public sealed class LoggingSignInDiagnostics(ILogger<LoggingSignInDiagnostics> logger) : ISignInDiagnostics
{
    /// <inheritdoc />
    public void Explain(SignInHint hint, OtpChannel channel, string destination) =>
        logger.LogWarning(
            "[DEV ONLY - never enabled outside Development] No code was sent for {MaskedDestination}: {Reason}",
            ContactMasker.Mask(channel, destination),
            ReasonFor(hint));

    /// <summary>The reason in words, with what to do about it.</summary>
    /// <param name="hint">The reason.</param>
    public static string ReasonFor(SignInHint hint) => hint switch
    {
        SignInHint.NoAccountForAddress => "no account has this address (a typo, or it never registered). Register first, or check the address.",
        SignInHint.AccountLocked => "the account is suspended or deactivated.",
        SignInHint.StaffMustUsePasswordAndCode => "this is a staff account. Staff use the Password tab, and then a code is sent.",
        SignInHint.CandidateHasNoPassword => "this account has no password. Candidates sign in with the One-time code tab.",
        SignInHint.WrongPassword => "the password is wrong.",
        _ => "unknown reason.",
    };
}
