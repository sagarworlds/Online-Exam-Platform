namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>How one question of an attempt was marked.</summary>
public enum AnswerVerdict
{
    /// <summary>The candidate chose the correct option.</summary>
    Correct,

    /// <summary>The candidate chose an option that is not correct.</summary>
    Wrong,

    /// <summary>The candidate chose nothing.</summary>
    Unanswered,
}
