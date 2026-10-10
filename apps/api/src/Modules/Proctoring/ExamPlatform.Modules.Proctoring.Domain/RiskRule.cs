namespace ExamPlatform.Modules.Proctoring.Domain;

/// <summary>
/// How one signal is judged: the value at which it counts as raised, which side of that value raises it, and the points it adds to the
/// score when it does. Weights and thresholds come from configuration, so a rule is data, not code (OCP).
/// </summary>
/// <param name="Kind">The signal the rule judges.</param>
/// <param name="Threshold">The value at which the signal is raised; compared with <paramref name="RaisedWhen"/>.</param>
/// <param name="RaisedWhen">Whether a value at or above, or at or below, the threshold raises the signal.</param>
/// <param name="Weight">The points the signal adds when raised. Zero means the signal is shown but never adds to the score.</param>
public sealed record RiskRule(RiskSignalKind Kind, decimal Threshold, RaisedWhen RaisedWhen, int Weight);
