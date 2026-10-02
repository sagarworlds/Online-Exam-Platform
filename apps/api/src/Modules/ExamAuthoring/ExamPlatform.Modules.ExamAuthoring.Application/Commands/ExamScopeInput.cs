using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Commands;

/// <summary>An exam scope as a caller asks for it, before it has been checked against the question bank.</summary>
/// <param name="Type">Anywhere, one whole book, or chosen chapters.</param>
/// <param name="BookId">The book, for a book or chapters scope.</param>
/// <param name="ChapterIds">The chosen chapters, for a chapters scope.</param>
public sealed record ExamScopeInput(ExamScopeType Type, Guid? BookId = null, IReadOnlyList<Guid>? ChapterIds = null);

/// <summary>Changes what an exam's questions may be drawn from.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="Scope">The new scope.</param>
public sealed record SetExamScopeCommand(Guid ExamId, ExamScopeInput Scope);
