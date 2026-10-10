namespace ExamPlatform.Modules.ExamRuntime.Contracts;

/// <summary>
/// The facts about one finished attempt that a risk review reads (FR-22, FR-26, FR-27). Every time is the server's clock.
/// </summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam it is an attempt at.</param>
/// <param name="CandidateId">The candidate who sat it.</param>
/// <param name="Number">Which attempt this is for the candidate, from 1.</param>
/// <param name="StartedAtUtc">When the candidate started it.</param>
/// <param name="SubmittedAtUtc">When it was submitted, by the candidate or by the deadline.</param>
/// <param name="IsInvalidated">Whether an administrator has invalidated the result (FR-29).</param>
/// <param name="AnsweredCount">How many questions carry a saved answer.</param>
/// <param name="FocusDepartures">How many times the candidate left the exam page (FR-22).</param>
/// <param name="ClientChanges">How many times the address or device signature changed during the attempt (FR-26).</param>
/// <param name="WrongAnswers">Each answer that was marked wrong, with the choice it was made with.</param>
public sealed record AttemptSignals(
    Guid AttemptId,
    Guid ExamId,
    Guid CandidateId,
    int Number,
    DateTime StartedAtUtc,
    DateTime SubmittedAtUtc,
    bool IsInvalidated,
    int AnsweredCount,
    int FocusDepartures,
    int ClientChanges,
    IReadOnlyList<WrongAnswer> WrongAnswers);

/// <summary>
/// A finished attempt, named without its answers: enough for a reviewer to decide whether the attempt is in scope before any answer is read.
/// </summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="CandidateId">The candidate who sat it.</param>
/// <param name="StartedAtUtc">When the candidate started it, by the server's clock.</param>
public sealed record FinishedAttemptRef(Guid AttemptId, Guid CandidateId, DateTime StartedAtUtc);

/// <summary>
/// One answer marked wrong. Two candidates who chose the same wrong answer to the same question share a <see cref="ChoiceKey"/>, which is
/// what the risk review compares between attempts.
/// </summary>
/// <param name="QuestionId">The question-bank id of the question answered.</param>
/// <param name="ChoiceKey">
/// The choice as a comparable key: the chosen option ids, sorted and joined, for a question answered by choosing options; or the typed
/// text, with its spaces collapsed and lower-cased, for a text question.
/// </param>
public sealed record WrongAnswer(Guid QuestionId, string ChoiceKey);
