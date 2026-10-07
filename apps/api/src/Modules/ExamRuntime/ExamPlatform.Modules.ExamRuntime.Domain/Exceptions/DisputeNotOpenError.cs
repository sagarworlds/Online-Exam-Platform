using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The dispute was already settled, so it cannot be settled again.</summary>
public sealed class DisputeNotOpenError() : DomainException("This dispute has already been settled.")
{
    /// <inheritdoc />
    public override string ErrorCode => "dispute_not_open";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
