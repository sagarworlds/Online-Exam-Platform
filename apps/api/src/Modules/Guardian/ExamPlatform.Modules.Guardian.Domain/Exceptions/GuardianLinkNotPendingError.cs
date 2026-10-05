using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Guardian.Domain.Exceptions;

/// <summary>Only a pending link can be verified (409).</summary>
public sealed class GuardianLinkNotPendingError() : DomainException("Only pending links can be verified.")
{
    /// <inheritdoc />
    public override string ErrorCode => "guardian_link_not_pending";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
