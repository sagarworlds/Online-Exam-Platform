namespace ExamPlatform.Modules.ExamAuthoring.Contracts;

/// <summary>
/// When candidates may see which of their answers were right, with the correct options. The contract's own copy of the
/// exam module's setting, so other modules never depend on its Domain.
/// </summary>
public enum ExamResultReleaseMode
{
    /// <summary>As soon as the attempt is submitted.</summary>
    Instant,

    /// <summary>From <see cref="ExamSnapshot.ResultReleaseTimeUtc"/>.</summary>
    Scheduled,

    /// <summary>When an administrator releases them, which sets <see cref="ExamSnapshot.ResultReleaseTimeUtc"/>.</summary>
    Manual,
}
