using ExamPlatform.Modules.Proctoring.Domain.Exceptions;

namespace ExamPlatform.Modules.Proctoring.Domain;

/// <summary>
/// The configured rules of the risk score: one <see cref="RiskRule"/> per signal, the score at which an attempt is flagged for review,
/// and the two limits that keep the answer-pattern and pace signals from judging thin evidence.
/// </summary>
/// <remarks>
/// Every value is checked when the policy is built, so a mistyped weight stops the host starting rather than quietly flagging nobody (or
/// everybody). The policy never changes an attempt; it only decides what goes into the review queue.
/// </remarks>
public sealed class RiskPolicy
{
    private readonly IReadOnlyDictionary<RiskSignalKind, RiskRule> _rules;

    private RiskPolicy(
        IReadOnlyDictionary<RiskSignalKind, RiskRule> rules, decimal flagThreshold, int minAnswersForPace, int maxSharersPerAnswer)
    {
        _rules = rules;
        FlagThreshold = flagThreshold;
        MinAnswersForPace = minAnswersForPace;
        MaxSharersPerAnswer = maxSharersPerAnswer;
    }

    /// <summary>The score at or above which an attempt is flagged for a human to review.</summary>
    public decimal FlagThreshold { get; }

    /// <summary>
    /// How many questions an attempt must have answered before its pace is judged. A candidate who answered three questions and stopped
    /// has a meaningless pace, so the signal is left unevaluated rather than scored as if it were a fast attempt.
    /// </summary>
    public int MinAnswersForPace { get; }

    /// <summary>
    /// The most candidates who may have chosen the same wrong answer for it to count as shared. Above this, the answer is a common
    /// mistake that many candidates make on their own, not evidence that two of them copied each other.
    /// </summary>
    public int MaxSharersPerAnswer { get; }

    /// <summary>The most points any attempt can score: the sum of every rule's weight.</summary>
    public int MaxScore => _rules.Values.Sum(r => r.Weight);

    /// <summary>The rule for one signal.</summary>
    /// <param name="kind">The signal.</param>
    /// <returns>Its rule.</returns>
    public RiskRule RuleFor(RiskSignalKind kind) => _rules[kind];

    /// <summary>
    /// Builds a policy from its parts and checks every one.
    /// </summary>
    /// <param name="rules">One rule for each signal kind, no more and no less.</param>
    /// <param name="flagThreshold">The score at which an attempt is flagged; more than zero and no more than the total weight, so that the flag is reachable and not raised for every attempt.</param>
    /// <param name="minAnswersForPace">At least 1.</param>
    /// <param name="maxSharersPerAnswer">At least 2: a shared answer has at least two candidates.</param>
    /// <returns>The policy.</returns>
    /// <exception cref="InvalidRiskPolicyError">A rule is missing, repeated or negative, or a limit is out of range.</exception>
    public static RiskPolicy Create(
        IEnumerable<RiskRule> rules, decimal flagThreshold, int minAnswersForPace, int maxSharersPerAnswer)
    {
        var list = rules.ToList();
        // Checked before the dictionary is built: ToDictionary would throw a generic exception on a repeated kind, not the domain error.
        if (list.Select(r => r.Kind).Distinct().Count() != list.Count)
            throw new InvalidRiskPolicyError("Each risk signal must be configured once.");

        var byKind = list.ToDictionary(r => r.Kind);

        var missing = Enum.GetValues<RiskSignalKind>().Where(kind => !byKind.ContainsKey(kind)).ToList();
        if (missing.Count > 0)
            throw new InvalidRiskPolicyError($"Risk signals missing from configuration: {string.Join(", ", missing)}.");

        foreach (var rule in list)
        {
            if (rule.Weight < 0)
                throw new InvalidRiskPolicyError($"The weight of {rule.Kind} must not be negative.");
            if (rule.Threshold < 0)
                throw new InvalidRiskPolicyError($"The threshold of {rule.Kind} must not be negative.");
        }

        var maxScore = list.Sum(r => r.Weight);
        // A zero threshold would flag every attempt; one above the total could never be reached, and the queue would stay empty for good.
        if (flagThreshold <= 0 || flagThreshold > maxScore)
            throw new InvalidRiskPolicyError($"The flag threshold must be above 0 and no more than the total weight ({maxScore}).");
        if (minAnswersForPace < 1)
            throw new InvalidRiskPolicyError("The minimum number of answers for the pace signal must be at least 1.");
        if (maxSharersPerAnswer < 2)
            throw new InvalidRiskPolicyError("The most sharers of an answer must be at least 2: a shared answer has two candidates.");

        return new RiskPolicy(byKind, flagThreshold, minAnswersForPace, maxSharersPerAnswer);
    }
}
