using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The attempt result was already invalidated.</summary>
public sealed class AttemptAlreadyInvalidatedError() : DomainException("This attempt result was already invalidated.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_already_invalidated";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
