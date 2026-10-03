namespace ExamPlatform.Modules.ExamAuthoring.Domain;

public record ExamConfig(
    int? TotalTimeSeconds,
    bool ShuffleQuestions,
    bool ShuffleOptions,
    bool SectionLockEnabled,
    bool CalculatorAllowed,
    bool ScratchpadAllowed,
    int MaxAttempts,
    int MaxRetakes,
    ResultReleaseMode ResultReleaseMode,
    DateTime? ResultReleaseTime,
    MarkingScheme MarkingScheme)
{
    /// <summary>The fewest attempts an exam can allow: every candidate can sit it once.</summary>
    public const int FewestAttempts = 1;

    /// <summary>
    /// The most attempts an exam can allow per candidate. A guard against a slipped digit rather than a product rule: an
    /// administrator can still give one candidate more, one at a time, once they have used what they have.
    /// </summary>
    public const int MostAttempts = 10;

    /// <summary>A new exam shuffles nothing on the first attempt: the author turns shuffling on, or later attempts shuffle anyway.</summary>
    public ExamConfig() : this(null, false, false, false, false, true, FewestAttempts, 0, ResultReleaseMode.Instant, null, new MarkingScheme()) { }
}
