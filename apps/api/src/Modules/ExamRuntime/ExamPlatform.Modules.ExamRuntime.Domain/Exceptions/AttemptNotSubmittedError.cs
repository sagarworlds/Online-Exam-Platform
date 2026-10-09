using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The attempt is still open, so there is nothing to review yet and the answer key must not leave the server.</summary>
public sealed class AttemptNotSubmittedError() : DomainException("The answers can be reviewed once the attempt is submitted.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_not_submitted";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
