namespace ExamPlatform.Modules.ExamRuntime.Contracts;

/// <summary>
/// How a candidate's answer to one question was marked, as another module sees it. Mirrors the marks ExamRuntime gives an answer, so a caller
/// never needs ExamRuntime's Domain to read them.
/// </summary>
public enum CandidateQuestionOutcome
{
    /// <summary>The candidate chose the correct option.</summary>
    Correct,

    /// <summary>A multiple-answer question the candidate got partly right, in an exam that gives partial credit.</summary>
    Partial,

    /// <summary>The candidate chose an option that is not correct.</summary>
    Wrong,

    /// <summary>The candidate chose nothing.</summary>
    Unanswered,
}

/// <summary>
/// One question of a released result: which section it sat in, how it was answered and how long the candidate spent on it. It carries no
/// question text, options or answer key.
/// </summary>
/// <param name="QuestionId">The question-bank id of the question.</param>
/// <param name="SectionId">The section of the exam the question sat in.</param>
/// <param name="Outcome">How the candidate's answer was marked.</param>
/// <param name="TimeSpentSeconds">
/// The running total of seconds the candidate spent on the question, as the page reported it; null when the page never reported on it.
/// </param>
public sealed record CandidateQuestionResult(
    Guid QuestionId,
    Guid SectionId,
    CandidateQuestionOutcome Outcome,
    int? TimeSpentSeconds);
