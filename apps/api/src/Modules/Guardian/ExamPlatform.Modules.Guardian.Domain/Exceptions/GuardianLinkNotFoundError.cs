using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Guardian.Domain.Exceptions;

/// <summary>The guardian has no link to this candidate (404).</summary>
public sealed class GuardianLinkNotFoundError() : DomainException("The guardian has no link to this candidate.")
{
    /// <inheritdoc />
    public override string ErrorCode => "guardian_link_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
