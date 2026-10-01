namespace ExamPlatform.Modules.ExamAuthoring.Application.Commands;

/// <summary>Creates a new exam in the draft state with the default configuration.</summary>
/// <param name="SeriesId">The exam series the exam belongs to, or <see langword="null"/> for a standalone exam; the empty GUID is rejected.</param>
/// <param name="Name">Display name of the exam; must not be blank.</param>
/// <param name="Description">Optional longer description shown to candidates.</param>
/// <param name="CreatedBy">The authoring user that creates the exam.</param>
public sealed record CreateExamCommand(
    Guid? SeriesId,
    string Name,
    string? Description,
    Guid CreatedBy);
