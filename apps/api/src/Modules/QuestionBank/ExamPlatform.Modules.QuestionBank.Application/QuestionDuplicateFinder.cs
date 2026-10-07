using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>A question already in the bank that says the same thing as one being added.</summary>
/// <param name="Question">The existing question.</param>
/// <param name="SameOptions">Whether it also has the same options, which makes it a duplicate rather than a question that merely starts the same way.</param>
public sealed record DuplicateMatch(Question Question, bool SameOptions);

/// <summary>
/// Finds questions the bank already holds that repeat a new one (FR-9). The one place the rule lives, so the single-question form, the
/// import and the check an author can ask for beforehand cannot disagree about what a duplicate is.
/// </summary>
/// <remarks>
/// Two levels, because a stem alone is a weak sign: "Which of these is a prime?" is asked with many different options. Same wording
/// and same options is a duplicate and stops a question being created unless the author says to add it anyway. Same wording with
/// other options is only reported, for the author to look at.
/// </remarks>
public sealed class QuestionDuplicateFinder(IQuestionRepository repository)
{
    /// <summary>The most matches reported for one question, so a bank full of copies does not return all of them.</summary>
    public const int MaxMatches = 10;

    /// <summary>Looks for questions that repeat the given one.</summary>
    /// <param name="plainText">The question's wording with the markup removed.</param>
    /// <param name="optionTexts">The texts of its options.</param>
    /// <param name="excludeQuestionId">A question to leave out, so a question being edited is not reported as a duplicate of itself.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Existing questions with the same wording, those with the same options first; empty when the wording has no readable text.</returns>
    public async Task<IReadOnlyList<DuplicateMatch>> FindAsync(
        string? plainText, IReadOnlyCollection<string> optionTexts, Guid? excludeQuestionId, CancellationToken cancellationToken)
    {
        var sameWording = await repository.FindByTextKeyAsync(QuestionFingerprint.KeyOf(plainText), excludeQuestionId, MaxMatches, cancellationToken);
        if (sameWording.Count == 0)
            return [];

        var optionsKey = QuestionFingerprint.OptionsKeyOf(optionTexts);
        return sameWording
            .Select(q => new DuplicateMatch(q, QuestionFingerprint.OptionsKeyOf(q.Options.Select(o => o.Text)) == optionsKey))
            .OrderByDescending(m => m.SameOptions)
            .ToList();
    }
}
