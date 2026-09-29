using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>The challenge has already been checked against the maximum allowed number of incorrect codes.</summary>
public sealed class OtpAttemptsExceededError() : DomainException("Too many incorrect attempts; request a new OTP.")
{
    /// <inheritdoc />
    public override string ErrorCode => "otp_attempts_exceeded";

    /// <inheritdoc />
    public override int HttpStatusCode => 429;
}
