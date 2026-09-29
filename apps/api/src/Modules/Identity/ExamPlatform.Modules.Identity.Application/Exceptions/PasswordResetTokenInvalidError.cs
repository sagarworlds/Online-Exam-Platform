using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>The password reset token is unknown, expired, or already used.</summary>
public sealed class PasswordResetTokenInvalidError() : DomainException("This password reset link is invalid or has expired.")
{
    /// <inheritdoc />
    public override string ErrorCode => "password_reset_token_invalid";
}
