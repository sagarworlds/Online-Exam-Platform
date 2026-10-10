namespace ExamPlatform.Modules.Proctoring.Domain;

/// <summary>
/// The facts the risk score is worked out from, for one finished attempt. A plain copy of what the exam runtime reports, so this module's
/// Domain never depends on another module's types.
/// </summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam it is an attempt at.</param>
/// <param name="CandidateId">The candidate who sat it.</param>
/// <param name="AttemptNumber">Which attempt this is for the candidate, from 1.</param>
/// <param name="StartedAtUtc">When the candidate started.</param>
/// <param name="FinishedAtUtc">When the attempt was submitted.</param>
/// <param name="Invalidated">Whether an administrator has invalidated the result.</param>
/// <param name="AnsweredCount">How many questions carry a saved answer.</param>
/// <param name="FocusDepartures">How many times the candidate left the exam page.</param>
/// <param name="ClientChanges">How many times the address or device changed during the attempt.</param>
/// <param name="WrongAnswers">Each answer marked wrong, with the choice it was made with.</param>
public sealed record AttemptRiskInputs(
    Guid AttemptId,
    Guid ExamId,
    Guid CandidateId,
    int AttemptNumber,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc,
    bool Invalidated,
    int AnsweredCount,
    int FocusDepartures,
    int ClientChanges,
    IReadOnlyList<WrongAnswerKey> WrongAnswers);

/// <summary>One wrong answer, keyed so that the same choice made by two candidates compares equal.</summary>
/// <param name="QuestionId">The question answered.</param>
/// <param name="ChoiceKey">The choice, as a comparable key.</param>
public sealed record WrongAnswerKey(Guid QuestionId, string ChoiceKey);
