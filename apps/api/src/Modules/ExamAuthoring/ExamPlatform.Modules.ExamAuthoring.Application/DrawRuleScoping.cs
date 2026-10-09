using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>
/// Folds an exam's scope into a draw rule. The rule says where an author wants questions from and the scope says where the exam
/// may take them from; a draw has to honour both, and doing the intersection here means the module that draws never sees scopes.
/// </summary>
public static class DrawRuleScoping
{
    /// <summary>Combines the rule with the exam's scope.</summary>
    /// <param name="rule">The author's rule.</param>
    /// <param name="scope">The exam's scope.</param>
    /// <returns>What to draw from, or null when the rule and the scope exclude each other, so nothing could ever match.</returns>
    public static DrawRuleSnapshot? Apply(SectionDrawRule rule, ExamScope scope)
    {
        switch (scope.Type)
        {
            case ExamScopeType.Independent:
                return new DrawRuleSnapshot(rule.Count, rule.BookId, rule.ChapterId, null, rule.Difficulty, rule.Topic);

            case ExamScopeType.Book:
                return rule.BookId is { } book && book != scope.BookId
                    ? null
                    : new DrawRuleSnapshot(rule.Count, scope.BookId, rule.ChapterId, null, rule.Difficulty, rule.Topic);

            case ExamScopeType.Chapters:
                if (rule.BookId is { } other && other != scope.BookId)
                    return null;
                // A rule that names a chapter outside the chosen ones can match nothing; one that names none draws from all of them.
                if (rule.ChapterId is { } chapter)
                    return scope.ChapterIds.Contains(chapter)
                        ? new DrawRuleSnapshot(rule.Count, scope.BookId, chapter, null, rule.Difficulty, rule.Topic)
                        : null;
                return new DrawRuleSnapshot(rule.Count, scope.BookId, null, scope.ChapterIds, rule.Difficulty, rule.Topic);

            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope.Type, "Unknown scope type.");
        }
    }
}
