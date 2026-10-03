using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>
/// The candidate has already made more attempts than the exam now allows, because its limit was lowered after they sat it. An extra
/// attempt granted now would still leave them over the limit and change nothing, so it is refused instead of recorded.
/// </summary>
public sealed class AttemptOverLimitError() : DomainException(
    "This candidate has already made more attempts than the exam now allows, because its limit was lowered. Raise the exam's limit to let them sit it again.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_over_limit";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
