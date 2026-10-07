namespace ExamPlatform.Modules.QuestionBank.Application.Dtos;

/// <summary>A class (a standard or grade) as the authoring side sees it.</summary>
/// <param name="Id">The class's id.</param>
/// <param name="Name">The class's name, for example "4th".</param>
/// <param name="IsArchived">Whether the class is archived (kept, but closed to new books).</param>
/// <param name="BookCount">How many books, archived ones included, belong to the class.</param>
/// <param name="CreatedAtUtc">When the class was created.</param>
public sealed record ClassDto(Guid Id, string Name, bool IsArchived, int BookCount, DateTime CreatedAtUtc);
