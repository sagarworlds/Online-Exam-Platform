using ExamPlatform.Modules.Proctoring.Domain;
using ExamPlatform.Modules.Proctoring.Endpoints;

namespace ExamPlatform.Modules.Proctoring.UnitTests;

/// <summary>Builds the attempts, policy and times the risk tests share, so each test states only what it changes.</summary>
internal static class RiskTestData
{
    /// <summary>The instant every attempt starts at in these tests.</summary>
    public static readonly DateTime Start = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>The policy the platform ships with, built from its own defaults so a change there shows up here.</summary>
    public static RiskPolicy DefaultPolicy { get; } = new RiskScoringOptions().ToPolicy();

    /// <summary>An attempt with twenty answers, no departures or changes, a pace of thirty seconds a question, and no wrong answers.</summary>
    public static AttemptRiskInputs Attempt(
        Guid? attemptId = null,
        Guid? examId = null,
        Guid? candidateId = null,
        int number = 1,
        int answered = 20,
        int elapsedSeconds = 600,
        int focus = 0,
        int changes = 0,
        bool invalidated = false,
        params WrongAnswerKey[] wrongAnswers) =>
        new(
            AttemptId: attemptId ?? Guid.NewGuid(),
            ExamId: examId ?? Guid.NewGuid(),
            CandidateId: candidateId ?? Guid.NewGuid(),
            AttemptNumber: number,
            StartedAtUtc: Start,
            FinishedAtUtc: Start.AddSeconds(elapsedSeconds),
            Invalidated: invalidated,
            AnsweredCount: answered,
            FocusDepartures: focus,
            ClientChanges: changes,
            WrongAnswers: wrongAnswers);

    /// <summary>The five rules of the default policy, in the order of <see cref="RiskSignalKind"/>.</summary>
    public static IReadOnlyList<RiskRule> DefaultRules() =>
    [
        new(RiskSignalKind.FocusDepartures, 3, RaisedWhen.AtLeast, 30),
        new(RiskSignalKind.ClientChanges, 2, RaisedWhen.AtLeast, 10),
        new(RiskSignalKind.Invalidated, 1, RaisedWhen.AtLeast, 15),
        new(RiskSignalKind.FastCompletion, 5, RaisedWhen.AtMost, 10),
        new(RiskSignalKind.SharedWrongAnswers, 3, RaisedWhen.AtLeast, 35),
    ];
}
