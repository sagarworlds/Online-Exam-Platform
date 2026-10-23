using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>Staff asked for a typed answer to be checked by the AI evaluator, which this server has no key for or has switched off.</summary>
public sealed class TextEvaluatorNotSetUpError() : DomainException("The AI evaluator is not set up on this server, so an answer cannot be checked by it.")
{
    /// <inheritdoc />
    public override string ErrorCode => "ai_evaluator_not_set_up";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
