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
    MarkingScheme MarkingScheme,
    bool ContentProtection,
    int FocusViolationLimit)
{
    /// <summary>The fewest attempts an exam can allow: every candidate can sit it once.</summary>
    public const int FewestAttempts = 1;

    /// <summary>
    /// The most attempts an exam can allow per candidate. A guard against a slipped digit rather than a product rule: an
    /// administrator can still give one candidate more, one at a time, once they have used what they have.
    /// </summary>
    public const int MostAttempts = 10;

    // ContentProtection: whether the exam page turns off copying, pasting, right-click and printing while a candidate sits the exam
    // (FR-23). On for a new exam, so protection is what a candidate gets unless an author deliberately lifts it, for example for practice.

    /// <summary>The fewest violations an exam can allow before it ends the attempt; 0 means the exam does not watch for them.</summary>
    public const int NoViolationLimit = 0;

    /// <summary>
    /// The most violations an exam can allow. A guard against a slipped digit: a limit much higher than this is not a limit a
    /// candidate will ever reach, so it would only look like detection while doing nothing.
    /// </summary>
    public const int MostViolations = 20;

    // FocusViolationLimit: how many times a candidate may leave the exam page (switch tab or window, or leave full screen) before the
    // server ends the attempt (FR-22). 0 turns detection off. Off for a new exam: ending somebody's sitting is a proctoring choice
    // an author makes on purpose, together with the consent text that tells candidates about it, never a default.

    /// <summary>A new exam shuffles nothing on the first attempt: the author turns shuffling on, or later attempts shuffle anyway.</summary>
    public ExamConfig() : this(null, false, false, false, false, true, FewestAttempts, 0, ResultReleaseMode.Instant, null, new MarkingScheme(), true, NoViolationLimit) { }
}
