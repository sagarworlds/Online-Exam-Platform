using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>An account already exists for the email address or phone number supplied at registration.</summary>
public sealed class DuplicateAccountError() : DomainException("An account already exists for this email or phone number.")
{
    /// <inheritdoc />
    public override string ErrorCode => "duplicate_account";
}
