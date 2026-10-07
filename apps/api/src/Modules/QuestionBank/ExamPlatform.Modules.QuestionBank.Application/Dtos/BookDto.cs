namespace ExamPlatform.Modules.QuestionBank.Application.Dtos;

/// <summary>A book as the authoring side sees it.</summary>
/// <param name="Id">The book's id.</param>
/// <param name="Name">The book's name.</param>
/// <param name="Subject">The subject it covers, or null.</param>
/// <param name="Description">A short description, or null.</param>
/// <param name="IsArchived">Whether the book is archived (kept, but closed to new chapters).</param>
/// <param name="Chapters">The chapters, in the order they were added.</param>
/// <param name="CreatedBy">The authoring user.</param>
/// <param name="CreatedAtUtc">When the book was created.</param>
/// <param name="ClassId">The class the book belongs to, or null when it has none.</param>
/// <param name="ClassName">That class's name, or null.</param>
public sealed record BookDto(
    Guid Id,
    string Name,
    string? Subject,
    string? Description,
    bool IsArchived,
    IReadOnlyList<ChapterDto> Chapters,
    Guid CreatedBy,
    DateTime CreatedAtUtc,
    Guid? ClassId = null,
    string? ClassName = null);

/// <summary>One chapter of a <see cref="BookDto"/>.</summary>
/// <param name="Id">The chapter's id.</param>
/// <param name="BookId">The book it belongs to.</param>
/// <param name="Title">The chapter's title.</param>
/// <param name="Order">Position in the book, from 1.</param>
/// <param name="IsArchived">Whether the chapter is archived.</param>
/// <param name="QuestionCount">How many questions are filed under it.</param>
public sealed record ChapterDto(Guid Id, Guid BookId, string Title, int Order, bool IsArchived, int QuestionCount);
