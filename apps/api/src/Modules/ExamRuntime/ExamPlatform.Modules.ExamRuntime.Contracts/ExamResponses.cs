namespace ExamPlatform.Modules.ExamRuntime.Contracts;

/// <summary>How the candidates of one exam answered it, from its released results (FR-37).</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="ResultsReleased">Whether the author has released the results, so that the attempts below count.</param>
/// <param name="QuestionIds">Every question that appeared on any counted paper: the exam's own order first, then questions drawn for candidates.</param>
/// <param name="Attempts">One entry per counted attempt, oldest submission first; empty while the results are held.</param>
public sealed record ExamResponses(
    Guid ExamId,
    string ExamName,
    bool ResultsReleased,
    IReadOnlyList<Guid> QuestionIds,
    IReadOnlyList<AttemptResponses> Attempts);

/// <summary>One counted attempt: who sat it, its score, and whether each question on its paper was answered fully correctly.</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="CandidateId">The candidate who sat it.</param>
/// <param name="SubmittedAtUtc">When it was submitted; breaks ties between a candidate's attempts with the same score.</param>
/// <param name="EndedByClock">Whether time ran out before the candidate submitted, rather than a violation or an administrator ending it (FR-37 drop-off).</param>
/// <param name="Score">The marks scored, as the latest version of the result says.</param>
/// <param name="MaxScore">The marks available on this attempt's paper, as the latest version of the result says. Drawn papers can differ in it.</param>
/// <param name="Questions">Each question on this attempt's paper, with whether it was answered fully correctly. A question left unanswered is not correct.</param>
public sealed record AttemptResponses(
    Guid AttemptId,
    Guid CandidateId,
    DateTime SubmittedAtUtc,
    bool EndedByClock,
    decimal Score,
    decimal MaxScore,
    IReadOnlyList<QuestionResponse> Questions);

/// <summary>Whether one question was answered fully correctly on one attempt, and how long the candidate spent on it.</summary>
/// <param name="QuestionId">The question.</param>
/// <param name="Correct">True only when the candidate's answer was fully correct; partly right, wrong and unanswered are all false.</param>
/// <param name="Answered">Whether the candidate left an answer to the question at all. An answer stays until it is cleared, so this is false for a question the candidate never answered.</param>
/// <param name="TimeSpentSeconds">
/// The running total of seconds the candidate spent on the question, as the page reported it; null when the page never reported on it. It is the
/// same time the candidate's own result shows (FR-36).
/// </param>
public sealed record QuestionResponse(Guid QuestionId, bool Correct, bool Answered, int? TimeSpentSeconds = null);
