namespace ExamPlatform.Modules.ExamRuntime.Endpoints;

/// <summary>The <c>ExamRuntime:Disputes</c> settings: how candidates may dispute an answer key (FR-31).</summary>
public sealed class DisputeOptions
{
    /// <summary>The configuration section these settings are read from.</summary>
    public const string SectionName = "ExamRuntime:Disputes";

    /// <summary>Days after a result is released that a candidate may still dispute it; 0 switches disputes off, and null falls back to the default.</summary>
    public int? WindowDays { get; set; }
}
