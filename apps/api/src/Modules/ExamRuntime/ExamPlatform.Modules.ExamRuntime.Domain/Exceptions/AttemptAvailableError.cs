using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The candidate still has an attempt they have not used, so another cannot be granted yet.</summary>
public sealed class AttemptAvailableError() : DomainException("This candidate still has an attempt left; another can be given once they have used it.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_available";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
