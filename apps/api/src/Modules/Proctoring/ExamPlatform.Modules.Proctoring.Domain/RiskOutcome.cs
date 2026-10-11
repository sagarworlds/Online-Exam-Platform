namespace ExamPlatform.Modules.Proctoring.Domain;

/// <summary>
/// The result of scoring one attempt: every signal's reading, the total, and whether the total reaches the flag threshold.
/// </summary>
/// <param name="Readings">One reading per signal kind, in the order of <see cref="RiskSignalKind"/>.</param>
/// <param name="Score">The points added up over the raised signals.</param>
/// <param name="MaxScore">The most points any attempt can score under the policy it was scored with.</param>
/// <param name="Flagged">Whether the score reaches the flag threshold. A flag only puts the attempt in the review queue.</param>
public sealed record RiskOutcome(IReadOnlyList<RiskSignalReading> Readings, int Score, int MaxScore, bool Flagged);
