namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>Where an <see cref="Attempt"/> stands.</summary>
public enum AttemptStatus
{
    /// <summary>The candidate is still working; answers can be saved.</summary>
    InProgress,

    /// <summary>The attempt is over and scored; nothing can change any more.</summary>
    Submitted,

    /// <summary>
    /// The attempt is over, and its typed answers are being checked by the AI evaluator, so it has no score yet. It is like
    /// <see cref="Submitted"/> in that nothing can change, and unlike it in that nothing that reads results counts it until the checking is
    /// done and it becomes <see cref="Submitted"/>. Appended last: the status is stored by name, and the order is not meaningful.
    /// </summary>
    Grading,
}
