namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>How one question of an attempt was marked.</summary>
public enum AnswerVerdict
{
    /// <summary>The candidate chose the correct option.</summary>
    Correct,

    /// <summary>
    /// A multiple-answer question the candidate got partly right, in an exam that gives partial credit: more of the correct options
    /// chosen than wrong ones, but not exactly the correct set.
    /// </summary>
    Partial,

    /// <summary>The candidate chose an option that is not correct.</summary>
    Wrong,

    /// <summary>The candidate chose nothing.</summary>
    Unanswered,
}
