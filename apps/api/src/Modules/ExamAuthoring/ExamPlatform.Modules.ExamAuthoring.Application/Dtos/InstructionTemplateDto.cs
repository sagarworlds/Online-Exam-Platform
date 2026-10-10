using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Dtos;

/// <summary>An instruction template as staff see it (FR-41).</summary>
/// <param name="Id">The template's id.</param>
/// <param name="Title">The name shown when choosing a template.</param>
/// <param name="Body">The instructions text an exam takes when it starts from this template.</param>
/// <param name="CreatedAtUtc">When it was created.</param>
/// <param name="UpdatedAtUtc">When it last changed.</param>
public sealed record InstructionTemplateDto(Guid Id, string Title, string Body, DateTime CreatedAtUtc, DateTime UpdatedAtUtc)
{
    /// <summary>Maps a template to its DTO.</summary>
    /// <param name="template">The template to map.</param>
    /// <returns>The DTO.</returns>
    public static InstructionTemplateDto From(InstructionTemplate template) =>
        new(template.Id, template.Title, template.Body, template.CreatedAtUtc, template.UpdatedAtUtc);
}
