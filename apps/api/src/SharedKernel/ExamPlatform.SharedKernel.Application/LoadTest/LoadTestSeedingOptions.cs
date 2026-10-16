namespace ExamPlatform.SharedKernel.Application.LoadTest;

/// <summary>
/// Settings for the commands that create the load test's candidate pool (<c>--create-load-test-candidates</c> and
/// <c>--enrol-load-test-candidates</c>, ADR 0006), bound from <c>LoadTest</c>. Both are meant for a staging environment only, so
/// nothing happens unless <see cref="Enabled"/> is set on purpose.
/// </summary>
public sealed class LoadTestSeedingOptions
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "LoadTest";

    /// <summary>
    /// Whether the load-test commands may run at all. Off by default and never set in <c>render.yaml</c>, so a production
    /// environment refuses them. It is the one thing that has to be done on purpose, on staging, for the job's run.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The password every account in the pool shares, which the load scripts sign in with. It is a test value for synthetic
    /// accounts, set in the job's environment (<c>LoadTest__Password</c>) for the run and removed afterwards.
    /// </summary>
    public string? Password { get; set; }
}
