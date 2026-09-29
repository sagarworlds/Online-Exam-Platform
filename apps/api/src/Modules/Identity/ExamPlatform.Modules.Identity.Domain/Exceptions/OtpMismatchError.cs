using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>The supplied OTP code did not match the challenge's code.</summary>
public sealed class OtpMismatchError() : DomainException("The OTP code is incorrect.")
{
    /// <inheritdoc />
    public override string ErrorCode => "otp_mismatch";
}
