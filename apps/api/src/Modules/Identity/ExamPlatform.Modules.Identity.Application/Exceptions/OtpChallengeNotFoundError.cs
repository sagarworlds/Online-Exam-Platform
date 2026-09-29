using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>No OTP challenge matches the id supplied (unknown, or already garbage-collected after expiry).</summary>
public sealed class OtpChallengeNotFoundError() : DomainException("This OTP challenge is unknown or has expired.")
{
    /// <inheritdoc />
    public override string ErrorCode => "otp_challenge_not_found";
}
