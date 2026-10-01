using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>The supplied OTP challenge was replaced by a newer one for the same destination and purpose, so its code is no longer accepted.</summary>
public sealed class OtpSupersededError() : DomainException("A newer code has been sent; use the most recent one.")
{
    /// <inheritdoc />
    public override string ErrorCode => "otp_superseded";
}
