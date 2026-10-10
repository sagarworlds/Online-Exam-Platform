using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// How an attempt reads its questions from the bank (FR-7): at the version it was sitting, so an edit made after it started
/// cannot change what the candidate saw, how it is marked or what its review shows. The one place that rule lives.
/// </summary>
public static class AttemptQuestionReads
{
    /// <summary>Reads the questions of an attempt, each at the version the attempt recorded for it.</summary>
    /// <remarks>
    /// An attempt with no recorded versions (made before they were kept, or a preview, which is never stored) reads the questions as
    /// they are now, which is all that was ever possible for it.
    /// </remarks>
    /// <param name="questionBank">The bank.</param>
    /// <param name="attempt">The attempt whose questions are wanted.</param>
    /// <param name="questionIds">The questions to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The questions found by id.</returns>
    public static async Task<Dictionary<Guid, QuestionSnapshot>> ReadAsync(
        this IQuestionBank questionBank, Attempt attempt, IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken)
    {
        var found = attempt.QuestionVersions.Count == 0
            ? await questionBank.GetAsync(questionIds, cancellationToken)
            : await questionBank.GetVersionsAsync(
                questionIds.Distinct().Select(id => new QuestionVersionRef(id, attempt.QuestionVersionOf(id))).ToList(), cancellationToken);

        return found.ToDictionary(q => q.Id);
    }

    /// <summary>
    /// Reads the questions of an attempt as a candidate is to see them, in the language they asked for where a translation exists (FR-51).
    /// </summary>
    /// <remarks>
    /// Display only. The translation's words replace the question's text and its options' texts, and nothing else: the option ids, which
    /// options are correct and which are pinned stay those of the version the attempt sat, so an answer is still an option id of the
    /// question the attempt was set, and marking and the shuffle order never depend on the language. A translation is paired with the
    /// options by position, so one with a different number of options (its source gained or lost one after it was written) is not used
    /// and the question is shown in its own language rather than with options that do not line up.
    /// </remarks>
    /// <param name="questionBank">The bank.</param>
    /// <param name="attempt">The attempt whose questions are wanted.</param>
    /// <param name="questionIds">The questions to read.</param>
    /// <param name="languages">The languages the candidate wants, best first; empty reads the questions as they are.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The questions found by id.</returns>
    public static async Task<Dictionary<Guid, QuestionSnapshot>> ReadForCandidateAsync(
        this IQuestionBank questionBank, Attempt attempt, IReadOnlyCollection<Guid> questionIds, IReadOnlyList<string> languages, CancellationToken cancellationToken)
    {
        var questions = await questionBank.ReadAsync(attempt, questionIds, cancellationToken);
        if (languages.Count == 0 || questions.Count == 0)
            return questions;

        foreach (var translation in await questionBank.GetTranslationsAsync(questions.Keys.ToList(), languages, cancellationToken))
        {
            if (!questions.TryGetValue(translation.QuestionId, out var question) || question.Options.Count != translation.OptionTexts.Count)
                continue;

            questions[translation.QuestionId] = question with
            {
                Text = translation.Text,
                Options = question.Options.Select((option, i) => option with { Text = translation.OptionTexts[i] }).ToList(),
                // The translation's own explanation, or none: the source's would be in another language beside translated wording.
                Explanation = translation.Explanation,
            };
        }

        return questions;
    }
}
