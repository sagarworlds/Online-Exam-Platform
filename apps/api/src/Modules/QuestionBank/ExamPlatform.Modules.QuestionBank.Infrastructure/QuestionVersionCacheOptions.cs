using ExamPlatform.Modules.QuestionBank.Application.Ports;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure;

/// <summary>How much of the bank's stored question versions the API keeps in memory (<c>QuestionBank:VersionCache</c>).</summary>
public sealed class QuestionVersionCacheOptions
{
    /// <summary>The configuration section these settings are read from.</summary>
    public const string SectionName = "QuestionBank:VersionCache";

    /// <summary>The most memory the cache may use for question content, in bytes, as <see cref="QuestionVersionContent.ApproximateBytes"/> counts it. 0 turns the cache off.</summary>
    public long MaxBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>How many minutes a version stays after it was last read, so an exam that is over stops holding memory.</summary>
    public int SlidingMinutes { get; set; } = 30;

    /// <summary>
    /// The most minutes a version stays however often it is read. A stored version never changes, so this is not about freshness: it bounds how
    /// long decrypted content stays in memory, and how long a host that had its database restored to an earlier point could go on serving
    /// content the restored database no longer holds.
    /// </summary>
    public int AbsoluteMinutes { get; set; } = 360;

    /// <summary>Whether the settings can be used.</summary>
    public bool IsValid => MaxBytes >= 0 && SlidingMinutes > 0 && AbsoluteMinutes > 0;
}
