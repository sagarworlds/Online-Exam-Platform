using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>The supplied OTP code was checked after its challenge's expiry.</summary>
public sealed class OtpExpiredError() : DomainException("The OTP code has expired.")
{
    /// <inheritdoc />
    public override string ErrorCode => "otp_expired";
}
