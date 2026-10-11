using System.ComponentModel.DataAnnotations;

namespace ExamPlatform.Modules.Identity.Application.Retention;

/// <summary>
/// How long a sign-in credential is kept after it expires (FR-47), read from the <c>Identity:Retention</c> section.
/// </summary>
/// <remarks>
/// The periods are provisional. The owner has not decided how long personal data is kept (the data map's open point OP1), so these
/// values are the engineering default until that decision is made, and they can be changed in configuration without a code change.
/// </remarks>
public sealed class CredentialRetentionOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Identity:Retention";

    /// <summary>
    /// Whether the sweep runs. When off, nothing is deleted automatically; the handler can still be run by hand.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Days a credential stays after it expires. It keeps the specific "expired" answer for a late attempt during this time; after it, the
    /// same attempt gets "not found". Zero deletes at the expiry instant.
    /// </summary>
    [Range(0, 3650)]
    public int GraceDays { get; set; } = 1;

    /// <summary>Minutes between two sweeps while the host is awake.</summary>
    [Range(1, 1440)]
    public int SweepMinutes { get; set; } = 60;
}
