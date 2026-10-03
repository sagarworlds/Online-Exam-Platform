namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>How hard an author judges a question to be. It is a label for finding questions, not part of any score.</summary>
public enum QuestionDifficulty
{
    /// <summary>A question most candidates should get right.</summary>
    Easy = 1,

    /// <summary>A question of ordinary difficulty.</summary>
    Medium = 2,

    /// <summary>A question that separates the strongest candidates.</summary>
    Hard = 3,
}
