namespace ExamPlatform.Modules.Proctoring.Domain;

/// <summary>
/// Finds the wrong answers that attempts at one exam share (FR-27): the same question, the same wrong choice, made by a few candidates.
/// </summary>
/// <remarks>
/// The signal counts only rare coincidences. A distractor that half the class picks is not evidence of anything; two candidates who both
/// pick the same unusual wrong combination, or type the same wrong number, are. The grouping is by question and choice, so it is linear
/// in the number of wrong answers: a group is never compared with more than <see cref="RiskPolicy.MaxSharersPerAnswer"/> members.
/// </remarks>
public static class SharedAnswerAnalyzer
{
    /// <summary>
    /// For each attempt, the largest number of rare identical wrong answers it shares with any single other attempt.
    /// </summary>
    /// <remarks>
    /// The maximum is taken over one partner, not summed over all of them: a candidate who copied one neighbour's paper has a high count
    /// with that neighbour, while the same answers spread thinly across the class would not reach it.
    /// </remarks>
    /// <param name="attempts">Every finished attempt at the exam.</param>
    /// <param name="maxSharers">The most attempts that may share a choice for it to count; from the policy.</param>
    /// <returns>The count for each attempt, zero when it shares no rare wrong answer with anyone.</returns>
    public static IReadOnlyDictionary<Guid, int> MaxSharedWithOneAttempt(IReadOnlyList<AttemptRiskInputs> attempts, int maxSharers)
    {
        var chosenBy = GroupChoices(attempts);
        var result = new Dictionary<Guid, int>(attempts.Count);
        foreach (var attempt in attempts)
            result[attempt.AttemptId] = MaxPartnerCount(attempt, chosenBy, maxSharers);

        return result;
    }

    /// <summary>Groups the attempts that chose each wrong answer, keyed by question and choice.</summary>
    private static Dictionary<(Guid QuestionId, string ChoiceKey), List<Guid>> GroupChoices(IReadOnlyList<AttemptRiskInputs> attempts)
    {
        var chosenBy = new Dictionary<(Guid, string), List<Guid>>();
        foreach (var attempt in attempts)
        {
            foreach (var wrong in attempt.WrongAnswers)
            {
                var key = (wrong.QuestionId, wrong.ChoiceKey);
                if (!chosenBy.TryGetValue(key, out var members))
                    chosenBy[key] = members = [];
                members.Add(attempt.AttemptId);
            }
        }

        return chosenBy;
    }

    /// <summary>Counts, for one attempt, the rare shared wrong answers each other attempt has, and returns the highest.</summary>
    private static int MaxPartnerCount(
        AttemptRiskInputs attempt, Dictionary<(Guid QuestionId, string ChoiceKey), List<Guid>> chosenBy, int maxSharers)
    {
        var partners = new Dictionary<Guid, int>();
        foreach (var wrong in attempt.WrongAnswers)
        {
            var members = chosenBy[(wrong.QuestionId, wrong.ChoiceKey)];
            // One candidate alone is no sharing, and a choice many candidates make is a common mistake, so neither counts.
            if (members.Count < 2 || members.Count > maxSharers)
                continue;

            foreach (var other in members)
            {
                if (other == attempt.AttemptId)
                    continue;
                partners[other] = partners.GetValueOrDefault(other) + 1;
            }
        }

        return partners.Values.DefaultIfEmpty(0).Max();
    }
}
