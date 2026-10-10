using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>An instruction template's title or text is blank or too long (FR-41).</summary>
/// <param name="message">What is wrong, in words a staff member can act on.</param>
public sealed class InvalidInstructionTemplateError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_instruction_template";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
