namespace ExamPlatform.Modules.ExamAuthoring.Application.Commands;

/// <summary>Creates an instruction template (FR-41).</summary>
/// <param name="Title">The template's name; required.</param>
/// <param name="Body">The instructions text; required.</param>
public sealed record CreateInstructionTemplateCommand(string? Title, string? Body);

/// <summary>Changes an instruction template (FR-41). Exams that already copied its text keep it.</summary>
/// <param name="TemplateId">The template to change.</param>
/// <param name="Title">The new name; required.</param>
/// <param name="Body">The new instructions text; required.</param>
public sealed record UpdateInstructionTemplateCommand(Guid TemplateId, string? Title, string? Body);

/// <summary>Deletes an instruction template (FR-41). Exams that copied its text are not affected.</summary>
/// <param name="TemplateId">The template to delete.</param>
public sealed record DeleteInstructionTemplateCommand(Guid TemplateId);

/// <summary>Sets an exam's instructions by hand (FR-41). Draft exams only.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="Instructions">The new text; blank clears it.</param>
public sealed record SetExamInstructionsCommand(Guid ExamId, string? Instructions);

/// <summary>Starts an exam's instructions from a template, by copying its text (FR-41). Draft exams only.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="TemplateId">The template to copy.</param>
public sealed record UseInstructionTemplateCommand(Guid ExamId, Guid TemplateId);
