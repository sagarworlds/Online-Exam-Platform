using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Dtos;

/// <summary>What an exam's questions may be drawn from, with the book and chapter names for display.</summary>
/// <param name="Type">Anywhere, one whole book, or chosen chapters.</param>
/// <param name="BookId">The book, for a book or chapters scope; otherwise null.</param>
/// <param name="BookName">The book's name, or null when there is no book or the question bank no longer has it.</param>
/// <param name="Chapters">The chosen chapters, for a chapters scope; empty otherwise.</param>
public sealed record ExamScopeDto(ExamScopeType Type, Guid? BookId, string? BookName, IReadOnlyList<ExamScopeChapterDto> Chapters);

/// <summary>One chosen chapter of an <see cref="ExamScopeDto"/>.</summary>
/// <param name="Id">The chapter's id.</param>
/// <param name="Title">The chapter's title, or null when the question bank no longer has it.</param>
public sealed record ExamScopeChapterDto(Guid Id, string? Title);
