namespace ExamPlatform.Modules.ExamAuthoring.Domain;

/// <summary>
/// Where a question is filed in the question bank: the book and chapter it belongs to. What an exam's scope is
/// checked against; this module only ever holds the ids, the bank owns what they mean.
/// </summary>
/// <param name="BookId">The book the question's chapter belongs to, or null when it is not filed.</param>
/// <param name="ChapterId">The chapter the question is filed under, or null when it is not filed.</param>
public readonly record struct QuestionPlacement(Guid? BookId, Guid? ChapterId)
{
    /// <summary>A question that is not filed under any chapter.</summary>
    public static QuestionPlacement Unfiled => default;
}
