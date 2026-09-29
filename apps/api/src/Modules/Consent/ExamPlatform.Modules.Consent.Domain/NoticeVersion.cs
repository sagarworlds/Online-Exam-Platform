using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Domain;

/// <summary>
/// One versioned revision of a consent notice's legal text. Consent records
/// reference a specific version, so a later revision never silently changes
/// what an existing consent grant is understood to cover (FR-44).
/// </summary>
public sealed class NoticeVersion : Entity
{
    /// <summary>What this notice covers.</summary>
    public ConsentPurpose Purpose { get; private set; }

    /// <summary>A human-readable version label (e.g. "2026-01-v2").</summary>
    public string VersionLabel { get; private set; }

    /// <summary>When this version became the active one to present to new consent requests.</summary>
    public DateTime EffectiveFromUtc { get; private set; }

    /// <summary>A pointer (URL or content hash) to the actual legal text — no legal copy is authored here.</summary>
    public string ContentReference { get; private set; }

    private NoticeVersion(
        Guid id, ConsentPurpose purpose, string versionLabel, DateTime effectiveFromUtc, string contentReference)
        : base(id)
    {
        Purpose = purpose;
        VersionLabel = versionLabel;
        EffectiveFromUtc = effectiveFromUtc;
        ContentReference = contentReference;
    }

    /// <summary>Creates a new notice version.</summary>
    /// <param name="purpose">What this notice covers.</param>
    /// <param name="versionLabel">A human-readable version label.</param>
    /// <param name="effectiveFromUtc">When this version takes effect.</param>
    /// <param name="contentReference">A pointer to the legal text (URL or content hash).</param>
    public static NoticeVersion Create(
        ConsentPurpose purpose, string versionLabel, DateTime effectiveFromUtc, string contentReference) =>
        new(Guid.NewGuid(), purpose, versionLabel, effectiveFromUtc, contentReference);
}
