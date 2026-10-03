namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>What another module may ask the bank to find questions by. Every part that is set must match.</summary>
/// <param name="BookId">Only questions filed under a chapter of this book.</param>
/// <param name="ChapterId">Only questions filed under this chapter.</param>
/// <param name="Difficulty">Only questions of this difficulty: "easy", "medium" or "hard".</param>
/// <param name="Topic">Only questions that carry this topic; case and surrounding spaces do not matter.</param>
/// <param name="ChapterIds">Only questions filed under one of these chapters; null or empty sets no limit. Combines with <paramref name="ChapterId"/>.</param>
public sealed record QuestionCriteria(
    Guid? BookId = null, Guid? ChapterId = null, string? Difficulty = null, string? Topic = null, IReadOnlyList<Guid>? ChapterIds = null);

/// <summary>Where a found question is filed: enough to choose among questions without reading each one.</summary>
/// <param name="Id">The question's id.</param>
/// <param name="ChapterId">The chapter it is filed under, or null.</param>
/// <param name="BookId">That chapter's book, or null.</param>
public sealed record FoundQuestion(Guid Id, Guid? ChapterId, Guid? BookId);
