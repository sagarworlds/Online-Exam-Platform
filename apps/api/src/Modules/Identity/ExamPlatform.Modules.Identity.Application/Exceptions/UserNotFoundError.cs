using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>No user matches the identifier, email, or phone number supplied.</summary>
public sealed class UserNotFoundError() : DomainException("No matching user was found.")
{
    /// <inheritdoc />
    public override string ErrorCode => "user_not_found";
}
