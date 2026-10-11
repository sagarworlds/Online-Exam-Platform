using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.Proctoring.Application.Dtos;
using ExamPlatform.Modules.Proctoring.Domain;

namespace ExamPlatform.Modules.Proctoring.Application;

/// <summary>Converts between what the exam runtime reports, the risk domain and the review queue's rows.</summary>
public static class RiskFlagMapper
{
    /// <summary>Copies one finished attempt's facts into the risk domain's own type.</summary>
    /// <param name="signals">The attempt, as the exam runtime reports it.</param>
    /// <returns>The same facts, as the risk score reads them.</returns>
    public static AttemptRiskInputs ToRiskInputs(AttemptSignals signals) =>
        new(
            AttemptId: signals.AttemptId,
            ExamId: signals.ExamId,
            CandidateId: signals.CandidateId,
            AttemptNumber: signals.Number,
            StartedAtUtc: signals.StartedAtUtc,
            FinishedAtUtc: signals.SubmittedAtUtc,
            Invalidated: signals.IsInvalidated,
            AnsweredCount: signals.AnsweredCount,
            FocusDepartures: signals.FocusDepartures,
            ClientChanges: signals.ClientChanges,
            WrongAnswers: signals.WrongAnswers.Select(w => new WrongAnswerKey(w.QuestionId, w.ChoiceKey)).ToList());

    /// <summary>Builds one row of the review queue from an assessment.</summary>
    /// <param name="assessment">The assessment, with its signals loaded.</param>
    /// <param name="candidateEmail">The candidate's address from the roster, or null when unknown.</param>
    /// <returns>The row, with every signal in the order it was judged.</returns>
    public static RiskFlagDto ToFlagDto(RiskAssessment assessment, string? candidateEmail) =>
        new(
            Id: assessment.Id,
            AttemptId: assessment.AttemptId,
            CandidateId: assessment.CandidateId,
            CandidateEmail: candidateEmail,
            AttemptNumber: assessment.AttemptNumber,
            Score: assessment.Score,
            MaxScore: assessment.MaxScore,
            Status: assessment.Status.ToString(),
            ComputedAtUtc: assessment.ComputedAtUtc,
            DecidedAtUtc: assessment.DecidedAtUtc,
            DecisionNote: assessment.DecisionNote,
            Signals: assessment.Signals.Select(ToSignalDto).ToList());

    private static RiskSignalDto ToSignalDto(RiskSignalReading reading) =>
        new(
            Kind: reading.Kind.ToString(),
            Value: reading.Value,
            Threshold: reading.Threshold,
            RaisedWhen: reading.RaisedWhen.ToString(),
            Weight: reading.Weight,
            Raised: reading.Raised,
            Points: reading.Points);
}
