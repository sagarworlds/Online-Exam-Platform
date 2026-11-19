using System.Diagnostics.CodeAnalysis;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application.Ports;

/// <summary>
/// The content of one stored version of a question, ready to be handed out: decrypted, with its options in display order. Nothing here is
/// about where the question is filed or whether it can be used now, because that changes without a new version.
/// </summary>
/// <param name="Text">The question text, as sanitized HTML.</param>
/// <param name="Options">The options in display order.</param>
/// <param name="AllowsMultiple">Whether more than one option may be correct.</param>
/// <param name="IsTextAnswer">Whether the candidate types the answer.</param>
/// <param name="AcceptedAnswers">For a text question, the answers a typed answer may be.</param>
/// <param name="Explanation">The explanation as plain text, or null for none.</param>
/// <param name="MatchRule">How strictly a typed answer is compared with the accepted answers.</param>
public sealed record QuestionVersionContent(
    string Text,
    IReadOnlyList<QuestionOptionSnapshot> Options,
    bool AllowsMultiple,
    bool IsTextAnswer,
    IReadOnlyList<string> AcceptedAnswers,
    string? Explanation,
    TextMatchRule MatchRule)
{
    /// <summary>
    /// About how much memory the content holds, for a cache that is limited by size. A string costs two bytes a character; the rest is a flat
    /// allowance for the objects around it. An estimate: it only has to be in proportion, so one question with pictures counts for far more than
    /// a hundred without.
    /// </summary>
    public long ApproximateBytes =>
        2L * (Text.Length + Options.Sum(o => o.Text.Length) + AcceptedAnswers.Sum(a => a.Length) + (Explanation?.Length ?? 0))
        + 64L * (Options.Count + AcceptedAnswers.Count + 2) + 128;
}

/// <summary>
/// Keeps the content of stored question versions in memory. A version is never edited once it is stored (FR-7), so what is kept under a
/// question and a version number is always right and is never invalidated; the cache only has to stay within its size.
/// </summary>
public interface IQuestionVersionCache
{
    /// <summary>Looks up a version's content.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="versionNumber">The version number.</param>
    /// <param name="content">The content, when it is held.</param>
    /// <returns><see langword="true"/> when the content is held.</returns>
    bool TryGet(Guid questionId, int versionNumber, [NotNullWhen(true)] out QuestionVersionContent? content);

    /// <summary>Keeps a version's content, unless the cache cannot hold it.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="versionNumber">The version number.</param>
    /// <param name="content">The content of exactly that stored version.</param>
    void Set(Guid questionId, int versionNumber, QuestionVersionContent content);
}

/// <summary>A cache that holds nothing: every read goes to the database. What a reader built without a cache uses.</summary>
public sealed class NoQuestionVersionCache : IQuestionVersionCache
{
    /// <summary>The one instance; it has no state.</summary>
    public static readonly NoQuestionVersionCache Instance = new();

    private NoQuestionVersionCache()
    {
    }

    /// <inheritdoc />
    public bool TryGet(Guid questionId, int versionNumber, [NotNullWhen(true)] out QuestionVersionContent? content)
    {
        content = null;
        return false;
    }

    /// <inheritdoc />
    public void Set(Guid questionId, int versionNumber, QuestionVersionContent content)
    {
    }
}
