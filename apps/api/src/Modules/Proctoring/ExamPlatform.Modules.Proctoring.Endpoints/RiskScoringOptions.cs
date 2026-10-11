using ExamPlatform.Modules.Proctoring.Domain;

namespace ExamPlatform.Modules.Proctoring.Endpoints;

/// <summary>
/// The configured weights and thresholds of the risk score, bound from <c>Proctoring:RiskScoring</c> (FR-27). The defaults here are the
/// values the platform ships with; a deployment overrides them in configuration. Which way a signal is judged is fixed in code, not
/// configured, so a mistyped setting cannot make a count of departures count as "too few".
/// </summary>
public sealed class RiskScoringOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Proctoring:RiskScoring";

    /// <summary>The score at or above which an attempt is flagged.</summary>
    public decimal FlagThreshold { get; set; } = 30;

    /// <summary>How many questions an attempt must have answered before its pace is judged.</summary>
    public int MinAnswersForPace { get; set; } = 10;

    /// <summary>The most candidates who may share a wrong answer for it to count as shared.</summary>
    public int MaxSharersPerAnswer { get; set; } = 2;

    /// <summary>Departures from the exam page (FR-22): raised at 3 or more.</summary>
    public RuleOptions FocusDepartures { get; set; } = new() { Threshold = 3, Weight = 30 };

    /// <summary>Changes of address or device during the attempt (FR-26): raised at 2 or more.</summary>
    public RuleOptions ClientChanges { get; set; } = new() { Threshold = 2, Weight = 10 };

    /// <summary>Invalidated results (FR-29): raised at 1 or more.</summary>
    public RuleOptions Invalidated { get; set; } = new() { Threshold = 1, Weight = 15 };

    /// <summary>Seconds per answered question: raised at 5 or fewer.</summary>
    public RuleOptions FastCompletion { get; set; } = new() { Threshold = 5, Weight = 10 };

    /// <summary>Rare identical wrong answers shared with one other candidate: raised at 3 or more.</summary>
    public RuleOptions SharedWrongAnswers { get; set; } = new() { Threshold = 3, Weight = 35 };

    /// <summary>
    /// Builds the policy the score is worked out with, checking every value.
    /// </summary>
    /// <returns>The policy.</returns>
    /// <exception cref="ExamPlatform.Modules.Proctoring.Domain.Exceptions.InvalidRiskPolicyError">A value is out of range.</exception>
    public RiskPolicy ToPolicy() =>
        RiskPolicy.Create(
            [
                FocusDepartures.ToRule(RiskSignalKind.FocusDepartures, RaisedWhen.AtLeast),
                ClientChanges.ToRule(RiskSignalKind.ClientChanges, RaisedWhen.AtLeast),
                Invalidated.ToRule(RiskSignalKind.Invalidated, RaisedWhen.AtLeast),
                FastCompletion.ToRule(RiskSignalKind.FastCompletion, RaisedWhen.AtMost),
                SharedWrongAnswers.ToRule(RiskSignalKind.SharedWrongAnswers, RaisedWhen.AtLeast),
            ],
            FlagThreshold,
            MinAnswersForPace,
            MaxSharersPerAnswer);

    /// <summary>The threshold and weight of one signal, as configured.</summary>
    public sealed class RuleOptions
    {
        /// <summary>The value at which the signal is raised.</summary>
        public decimal Threshold { get; set; }

        /// <summary>The points the signal adds when raised.</summary>
        public int Weight { get; set; }

        /// <summary>Turns the configured values into a rule for one signal.</summary>
        /// <param name="kind">The signal.</param>
        /// <param name="raisedWhen">Which side of the threshold raises it; fixed per signal, not configured.</param>
        /// <returns>The rule.</returns>
        public RiskRule ToRule(RiskSignalKind kind, RaisedWhen raisedWhen) => new(kind, Threshold, raisedWhen, Weight);
    }
}
