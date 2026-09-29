using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>The account exists but is suspended or deactivated and cannot log in.</summary>
public sealed class AccountLockedError() : DomainException("This account cannot currently log in.")
{
    /// <inheritdoc />
    public override string ErrorCode => "account_locked";

    /// <inheritdoc />
    public override int HttpStatusCode => 403;
}
