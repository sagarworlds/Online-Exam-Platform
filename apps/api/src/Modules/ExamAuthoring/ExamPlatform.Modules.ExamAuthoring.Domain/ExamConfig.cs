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
    public ExamConfig() : this(null, true, true, false, false, true, 1, 0, ResultReleaseMode.Instant, null, new MarkingScheme()) { }
}
