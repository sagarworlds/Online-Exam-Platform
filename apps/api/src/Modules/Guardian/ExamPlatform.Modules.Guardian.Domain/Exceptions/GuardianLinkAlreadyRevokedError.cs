using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Guardian.Domain.Exceptions;

/// <summary>The link was already revoked (409).</summary>
public sealed class GuardianLinkAlreadyRevokedError() : DomainException("This link is already revoked.")
{
    /// <inheritdoc />
    public override string ErrorCode => "guardian_link_already_revoked";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
