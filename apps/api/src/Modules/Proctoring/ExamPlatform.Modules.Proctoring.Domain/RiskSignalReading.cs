namespace ExamPlatform.Modules.Proctoring.Domain;

/// <summary>
/// One signal as it was judged for one attempt: the value read, the rule it was judged against and the points it added. Stored with the
/// assessment so a reviewer sees why an attempt was flagged, in numbers, rather than a bare score.
/// </summary>
/// <param name="Kind">The signal.</param>
/// <param name="Value">The value read, or null when the signal could not be judged (the pace of an attempt with too few answers).</param>
/// <param name="Threshold">The threshold it was judged against.</param>
/// <param name="RaisedWhen">Which side of the threshold raises it.</param>
/// <param name="Weight">The weight of its rule.</param>
/// <param name="Raised">Whether the value crossed the threshold.</param>
/// <param name="Points">The points it added to the score: the weight when raised, otherwise zero.</param>
public sealed record RiskSignalReading(
    RiskSignalKind Kind,
    decimal? Value,
    decimal Threshold,
    RaisedWhen RaisedWhen,
    int Weight,
    bool Raised,
    int Points)
{
    /// <summary>Judges a value against its rule.</summary>
    /// <param name="rule">The rule to judge by.</param>
    /// <param name="value">The value read, or null when it could not be read.</param>
    /// <returns>The reading. An unjudged value is never raised and adds no points.</returns>
    public static RiskSignalReading Judge(RiskRule rule, decimal? value)
    {
        var raised = value is { } v && (rule.RaisedWhen == RaisedWhen.AtLeast ? v >= rule.Threshold : v <= rule.Threshold);
        return new RiskSignalReading(
            rule.Kind, value, rule.Threshold, rule.RaisedWhen, rule.Weight, raised, raised ? rule.Weight : 0);
    }
}
