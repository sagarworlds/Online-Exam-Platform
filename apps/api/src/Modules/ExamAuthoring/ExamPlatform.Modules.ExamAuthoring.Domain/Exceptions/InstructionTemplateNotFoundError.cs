using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>No instruction template has that id (FR-41).</summary>
/// <param name="templateId">The id that was asked for.</param>
public sealed class InstructionTemplateNotFoundError(Guid templateId)
    : DomainException($"No instruction template has the id {templateId}.")
{
    /// <inheritdoc />
    public override string ErrorCode => "instruction_template_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
