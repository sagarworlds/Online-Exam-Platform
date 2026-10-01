using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>
/// The OTP challenge was issued for a purpose that cannot complete a sign-in (e.g. a
/// password-reset code presented to the login verify endpoint).
/// </summary>
public sealed class OtpPurposeNotAllowedError() : DomainException("This one-time code cannot be used to sign in.")
{
    /// <inheritdoc />
    public override string ErrorCode => "otp_purpose_not_allowed";
}
