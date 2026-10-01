using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>The supplied OTP challenge was already consumed by an earlier successful verify, so its code cannot be replayed.</summary>
public sealed class OtpAlreadyUsedError() : DomainException("This code has already been used; request a new one.")
{
    /// <inheritdoc />
    public override string ErrorCode => "otp_already_used";
}
