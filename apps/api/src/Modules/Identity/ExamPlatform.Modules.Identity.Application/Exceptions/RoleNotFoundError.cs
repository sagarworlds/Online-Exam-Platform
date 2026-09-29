using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>No role matches the identifier or name supplied.</summary>
public sealed class RoleNotFoundError() : DomainException("No matching role was found.")
{
    /// <inheritdoc />
    public override string ErrorCode => "role_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
