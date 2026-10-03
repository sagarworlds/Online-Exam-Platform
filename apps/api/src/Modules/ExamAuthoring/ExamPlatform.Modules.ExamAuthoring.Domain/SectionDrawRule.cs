using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamAuthoring.Domain;

/// <summary>
/// "Draw this many questions at random from here": a rule of a section that is filled in for each candidate when they start an
/// attempt, on top of the section's fixed questions. The rule names where the questions come from by their place in the bank (book,
/// chapter) and their labels (difficulty, topic); every part that is set must match, and a part left out matches anything.
/// </summary>
public class SectionDrawRule
{
    /// <summary>The most questions one rule may draw.</summary>
    public const int MaxCount = 100;

    /// <summary>The longest topic, after trimming; the question bank limits topics to the same length.</summary>
    public const int MaxTopicLength = 40;

    /// <summary>The rule's id.</summary>
    public Guid Id { get; set; }

    /// <summary>The section it belongs to.</summary>
    public Guid SectionId { get; set; }

    /// <summary>Position among the section's rules, from 1.</summary>
    public int Order { get; set; }

    /// <summary>How many questions to draw, from 1 to <see cref="MaxCount"/>.</summary>
    public int Count { get; set; }

    /// <summary>Only questions of this book, or null for any.</summary>
    public Guid? BookId { get; set; }

    /// <summary>Only questions of this chapter, or null for any.</summary>
    public Guid? ChapterId { get; set; }

    /// <summary>Only questions of this difficulty ("easy", "medium" or "hard"), or null for any.</summary>
    public string? Difficulty { get; set; }

    /// <summary>Only questions with this topic, lower case, or null for any.</summary>
    public string? Topic { get; set; }

    /// <summary>When the rule was added.</summary>
    public DateTime CreatedAt { get; set; }

    private SectionDrawRule() { }

    internal SectionDrawRule(Guid sectionId, int order, int count, Guid? bookId, Guid? chapterId, string? difficulty, string? topic)
    {
        Id = Guid.NewGuid();
        SectionId = sectionId;
        Order = order;
        Count = count;
        BookId = bookId;
        ChapterId = chapterId;
        Difficulty = difficulty;
        Topic = topic;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Checks a rule's parts and puts them in the form they are stored in.</summary>
    /// <param name="count">How many to draw.</param>
    /// <param name="bookId">The book, or null for any; an empty id counts as none.</param>
    /// <param name="chapterId">The chapter, or null for any; an empty id counts as none.</param>
    /// <param name="difficulty">The difficulty, or blank for any.</param>
    /// <param name="topic">The topic, or blank for any.</param>
    /// <returns>The cleaned parts: no empty ids, a lower-case difficulty and a trimmed, lower-case topic, blank ones as null.</returns>
    /// <exception cref="InvalidExamConfigError">The count is outside 1 to <see cref="MaxCount"/>, the difficulty is not a level, or the topic is too long.</exception>
    public static (Guid? BookId, Guid? ChapterId, string? Difficulty, string? Topic) Clean(
        int count, Guid? bookId, Guid? chapterId, string? difficulty, string? topic)
    {
        if (count is < 1 or > MaxCount)
            throw new InvalidExamConfigError($"A rule draws between 1 and {MaxCount} questions.");

        var level = string.IsNullOrWhiteSpace(difficulty) ? null : difficulty.Trim().ToLowerInvariant();
        if (level is not (null or "easy" or "medium" or "hard"))
            throw new InvalidExamConfigError("The difficulty must be easy, medium or hard.");

        // Whitespace collapsed and lower case, as the question bank stores topics, so a rule finds what an author typed.
        var cleanTopic = string.Join(' ', (topic ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        if (cleanTopic.Length > MaxTopicLength)
            throw new InvalidExamConfigError($"A topic must be at most {MaxTopicLength} characters.");

        return (
            bookId == Guid.Empty ? null : bookId,
            chapterId == Guid.Empty ? null : chapterId,
            level,
            cleanTopic.Length == 0 ? null : cleanTopic);
    }
}
