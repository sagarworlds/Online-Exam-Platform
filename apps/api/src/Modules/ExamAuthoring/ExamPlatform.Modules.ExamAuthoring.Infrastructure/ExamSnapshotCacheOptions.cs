namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure;

/// <summary>How long a host keeps an exam's snapshot in memory (<c>ExamAuthoring:SnapshotCache</c>).</summary>
public sealed class ExamSnapshotCacheOptions
{
    /// <summary>The configuration section these settings are read from.</summary>
    public const string SectionName = "ExamAuthoring:SnapshotCache";

    /// <summary>
    /// How many seconds a snapshot is served before the exam is read again. A change made on this host shows at once whatever this is; it is the
    /// longest a change made through another host can go unseen here. 0 turns the cache off.
    /// </summary>
    public int LifetimeSeconds { get; set; } = 15;

    /// <summary>The most exams kept at once; reaching it starts the cache again.</summary>
    public int MaxEntries { get; set; } = 500;

    /// <summary>Whether the settings can be used.</summary>
    public bool IsValid => LifetimeSeconds is >= 0 and <= 3600 && MaxEntries >= 1;
}
