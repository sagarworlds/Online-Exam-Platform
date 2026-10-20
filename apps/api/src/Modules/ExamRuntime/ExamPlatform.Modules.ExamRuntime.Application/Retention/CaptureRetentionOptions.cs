using System.ComponentModel.DataAnnotations;

namespace ExamPlatform.Modules.ExamRuntime.Application.Retention;

/// <summary>
/// How long webcam photos and screen pictures are kept (FR-47, FR-24, FR-25), read from the <c>ExamRuntime:CaptureRetention</c> section.
/// </summary>
/// <remarks>
/// The period runs from the moment each capture was taken, not from when results are released: release depends on each exam's release mode,
/// and a fixed age needs no exam lookup. The default of 30 days is the requirements' figure for proctoring media (section 7.3). It covers the
/// 7-day dispute window with room to spare, and it can be changed in configuration without a code change.
/// </remarks>
public sealed class CaptureRetentionOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "ExamRuntime:CaptureRetention";

    /// <summary>Whether the sweep runs. When off, nothing is deleted automatically; the handler can still be run by hand.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Days a capture is kept after it was taken.</summary>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 30;

    /// <summary>Minutes between two sweeps while the host is awake.</summary>
    [Range(1, 1440)]
    public int SweepMinutes { get; set; } = 60;
}
