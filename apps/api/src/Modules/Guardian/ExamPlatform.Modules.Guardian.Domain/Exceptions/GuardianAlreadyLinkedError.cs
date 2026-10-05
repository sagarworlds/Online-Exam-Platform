using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Guardian.Domain.Exceptions;

/// <summary>The guardian already has a link to this candidate (409).</summary>
public sealed class GuardianAlreadyLinkedError() : DomainException("Guardian is already linked to this candidate.")
{
    /// <inheritdoc />
    public override string ErrorCode => "guardian_already_linked";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
