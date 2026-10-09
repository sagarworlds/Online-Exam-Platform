namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>Where an <see cref="Attempt"/> stands.</summary>
public enum AttemptStatus
{
    /// <summary>The candidate is still working; answers can be saved.</summary>
    InProgress,

    /// <summary>The attempt is over and scored; nothing can change any more.</summary>
    Submitted,
}
