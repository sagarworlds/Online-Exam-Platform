using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>The session named does not belong to this user, or does not exist.</summary>
public sealed class SessionNotFoundError() : DomainException("No matching session was found for this account.")
{
    /// <inheritdoc />
    public override string ErrorCode => "session_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
