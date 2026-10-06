using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>Describes an exam's proctoring, and the profiles on offer, the one way for every reader (FR-46).</summary>
internal static class ProctoringMapping
{
    /// <summary>The exam's proctoring: which profile its settings amount to, and the notice written from them.</summary>
    /// <param name="config">The exam's configuration.</param>
    public static ProctoringDto For(ExamConfig config)
    {
        var id = ProctoringProfiles.IdFor(config.ContentProtection, config.FocusViolationLimit);
        return new ProctoringDto(
            id,
            ProctoringProfiles.Find(id)?.Name ?? "Custom",
            ProctoringNotice.For(config.ContentProtection, config.FocusViolationLimit));
    }

    /// <summary>A profile as an author chooses it, with the notice candidates would be shown under it.</summary>
    /// <param name="profile">The profile.</param>
    public static ProctoringProfileDto ToDto(ProctoringProfile profile) =>
        new(
            profile.Id,
            profile.Name,
            profile.Description,
            profile.Available,
            profile.UnavailableReason,
            profile.ContentProtection,
            profile.FocusViolationLimit,
            ProctoringNotice.For(profile.ContentProtection, profile.FocusViolationLimit));
}
