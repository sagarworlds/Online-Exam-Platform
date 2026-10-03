using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>The bank does not have enough questions for the exam's draw rules, so publishing it would give candidates papers that cannot be made.</summary>
public sealed class DrawPoolTooSmallError(string problem) : DomainException(problem)
{
    /// <inheritdoc />
    public override string ErrorCode => "draw_pool_too_small";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
