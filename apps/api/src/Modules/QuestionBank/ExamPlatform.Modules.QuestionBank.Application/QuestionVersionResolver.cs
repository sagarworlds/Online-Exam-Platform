using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>What <see cref="QuestionVersionResolver"/> found for a set of version references.</summary>
/// <param name="Found">Questions whose stored version was read, with the version number and its content.</param>
/// <param name="AsItIsNow">Questions with no stored version to read: from before versions were kept, or a version number never stored.</param>
internal sealed record ResolvedVersions(
    IReadOnlyDictionary<Guid, (int Number, QuestionVersionContent Content)> Found, IReadOnlyList<Guid> AsItIsNow);

/// <summary>
/// Finds the stored content of the question versions asked for: from the cache where it is held, otherwise from the stored version rows,
/// which it then keeps. A stored version never changes (FR-7), so nothing it keeps can go out of date.
/// </summary>
internal sealed class QuestionVersionResolver(IQuestionRepository repository, IQuestionVersionCache cache)
{
    /// <summary>Resolves one version reference per question.</summary>
    /// <param name="wanted">One reference per question.</param>
    /// <param name="facts">The plain-column facts of the questions that exist; a question not in it does not exist and is skipped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ResolvedVersions> ResolveAsync(
        IReadOnlyList<QuestionVersionRef> wanted, IReadOnlyDictionary<Guid, QuestionFacts> facts, CancellationToken cancellationToken)
    {
        var found = new Dictionary<Guid, (int Number, QuestionVersionContent Content)>();
        var missed = new List<(Guid QuestionId, int VersionNumber)>();
        var asItIsNow = new List<Guid>();

        foreach (var reference in wanted)
        {
            if (!facts.TryGetValue(reference.QuestionId, out var fact))
                continue;

            // No version was ever stored (a question from before versions were kept), or the one asked for never was: it reads as it is now.
            var number = reference.VersionNumber ?? fact.CurrentVersion;
            if (fact.CurrentVersion is null || number is null || number < 1 || number > fact.CurrentVersion)
            {
                asItIsNow.Add(reference.QuestionId);
                continue;
            }

            if (cache.TryGet(reference.QuestionId, number.Value, out var kept))
                found[reference.QuestionId] = (number.Value, kept);
            else
                missed.Add((reference.QuestionId, number.Value));
        }

        if (missed.Count > 0)
            await ReadStoredAsync(missed, found, asItIsNow, cancellationToken);

        return new ResolvedVersions(found, asItIsNow);
    }

    // Read from the stored version, which cannot change under the read, and not from the question, which an edit could change between
    // learning the number and reading the text: what is kept under a number must be the content of that number.
    private async Task ReadStoredAsync(
        List<(Guid QuestionId, int VersionNumber)> missed,
        Dictionary<Guid, (int Number, QuestionVersionContent Content)> found,
        List<Guid> asItIsNow,
        CancellationToken cancellationToken)
    {
        var stored = (await repository.GetVersionsAsync(missed, cancellationToken)).ToDictionary(v => (v.QuestionId, v.VersionNumber));
        foreach (var (questionId, number) in missed)
        {
            if (!stored.TryGetValue((questionId, number), out var version))
            {
                asItIsNow.Add(questionId);
                continue;
            }

            var content = ContentOf(version);
            cache.Set(questionId, number, content);
            found[questionId] = (number, content);
        }
    }

    // The lists are handed to every reader of the cached content, so they are read-only wrappers: a list or an array that a caller could cast
    // back and sort or edit in place would change the content for everyone after it.
    private static QuestionVersionContent ContentOf(QuestionVersion v) =>
        new(
            v.Text,
            v.Options.OrderBy(o => o.Order).Select(o => new QuestionOptionSnapshot(o.OptionId, o.Text, o.IsCorrect, o.IsPinned)).ToList().AsReadOnly(),
            v.AllowsMultiple,
            v.IsTextAnswer,
            Array.AsReadOnly(v.AcceptedAnswers),
            v.Explanation,
            v.MatchRule);
}
