using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>A new attempt was asked for without the candidate having read and acknowledged the exam's instructions (400).</summary>
public sealed class InstructionsNotAcknowledgedError() : DomainException("Read the instructions and confirm you understand them before starting the exam.")
{
    /// <inheritdoc />
    public override string ErrorCode => "instructions_not_acknowledged";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
