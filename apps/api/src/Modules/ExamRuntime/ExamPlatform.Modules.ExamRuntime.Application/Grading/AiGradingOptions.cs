using System.ComponentModel.DataAnnotations;

namespace ExamPlatform.Modules.ExamRuntime.Application.Grading;

/// <summary>
/// How the host works through the typed answers that wait for the AI evaluator, read from the <c>ExamRuntime:AiGrading</c> section. What the
/// evaluator itself is (the key, the model) is the <c>Gemini</c> section; with no key nothing waits and none of this is used.
/// </summary>
public sealed class AiGradingOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "ExamRuntime:AiGrading";

    /// <summary>Whether this host works through the queue. Off, the answers wait for a host that does, and no result is lost.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between two looks at the queue while the host is awake.</summary>
    [Range(1, 3600)]
    public int PollSeconds { get; set; } = 10;

    /// <summary>How many answers are asked about at once. Kept low so the evaluator's own rate limit is not what the exam runs into.</summary>
    [Range(1, 20)]
    public int Concurrency { get; set; } = 3;

    /// <summary>Minutes a host holds an answer it took from the queue, so that two hosts do not ask about the same one. A host that dies lets go when this passes.</summary>
    [Range(1, 120)]
    public int LeaseMinutes { get; set; } = 5;

    /// <summary>Seconds to wait after the first failure before the answer is asked about again. Each later wait doubles, up to <see cref="RetryMaxMinutes"/>.</summary>
    [Range(1, 3600)]
    public int RetryBaseSeconds { get; set; } = 30;

    /// <summary>The longest wait between two tries of one answer, in minutes. A failure that waiting cannot cure (a key the service refuses) always waits this long.</summary>
    [Range(1, 1440)]
    public int RetryMaxMinutes { get; set; } = 10;

    /// <summary>
    /// The longest an attempt waits in grading for the evaluator, in minutes, counted from when its typed answers were queued. After that each
    /// answer still without a verdict is marked by the accepted answers alone, says so on the answer, and the attempt is scored; staff can have
    /// such an answer checked again later. 0 never gives up: the attempt waits for as long as it takes, however long the evaluator is down.
    /// </summary>
    [Range(0, 10080)]
    public int MaxWaitMinutes { get; set; } = 30;
}
