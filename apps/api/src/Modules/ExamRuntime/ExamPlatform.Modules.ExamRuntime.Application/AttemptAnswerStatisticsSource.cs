using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Tells the question bank how candidates have answered a question (FR-9). An answer is judged against the question as the attempt sat
/// it, so a key corrected later does not change what an old attempt got right.
/// </summary>
/// <remarks>
/// "Fully correct" means the candidate chose exactly the correct options. It is not the marks: a marking scheme gives partial credit or
/// takes marks off, and that belongs to an exam, while a question's difficulty is the same wherever it is used.
/// </remarks>
public sealed class AttemptAnswerStatisticsSource(IAttemptRepository attempts, IQuestionBank questionBank) : IQuestionStatisticsSource
{
    /// <inheritdoc />
    public async Task<QuestionAnswerStatistics> ReadAsync(Guid questionId, CancellationToken cancellationToken)
    {
        var answers = await attempts.ListSubmittedAnswersAsync(questionId, cancellationToken);
        if (answers.Count == 0)
            return new QuestionAnswerStatistics(0, 0, new Dictionary<Guid, int>());

        // One read of the question for each version the attempts sat, however many answers there are.
        var versions = await questionBank.GetVersionsAsync(
            answers.Select(a => a.VersionNumber).Distinct().Select(v => new QuestionVersionRef(questionId, v)).ToList(), cancellationToken);
        var current = (await questionBank.GetAsync([questionId], cancellationToken)).FirstOrDefault();

        // Two requests can come back as the same version (the current one asked for by number and as "current"), so keep the first.
        var correctByVersion = new Dictionary<int, HashSet<Guid>>();
        var acceptedByVersion = new Dictionary<int, string[]>();
        foreach (var v in versions)
        {
            correctByVersion.TryAdd(v.VersionNumber, v.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet());
            acceptedByVersion.TryAdd(v.VersionNumber, (v.AcceptedAnswers ?? []).ToArray());
        }
        var correctNow = current?.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
        var acceptedNow = (current?.AcceptedAnswers ?? []).ToArray();

        var correct = 0;
        var chosen = new Dictionary<Guid, int>();
        foreach (var answer in answers)
        {
            // An attempt made before versions were recorded read the question as it was then; the current key is all that is left of it.
            var version = answer.VersionNumber;

            // A typed answer is right when it matches an accepted answer of the version it was given against. It has no option to count.
            if (answer.AnswerText is { } typed)
            {
                var accepted = version is { } acceptedVersion && acceptedByVersion.TryGetValue(acceptedVersion, out var known) ? known : acceptedNow;
                if (TypedAnswer.Matches(typed, accepted))
                    correct++;
                continue;
            }

            var key = version is { } choiceVersion && correctByVersion.TryGetValue(choiceVersion, out var knownKey) ? knownKey : correctNow;
            if (key is not null && key.SetEquals(answer.SelectedOptionIds))
                correct++;

            foreach (var optionId in answer.SelectedOptionIds.Distinct())
                chosen[optionId] = chosen.GetValueOrDefault(optionId) + 1;
        }

        return new QuestionAnswerStatistics(answers.Count, correct, chosen);
    }
}
