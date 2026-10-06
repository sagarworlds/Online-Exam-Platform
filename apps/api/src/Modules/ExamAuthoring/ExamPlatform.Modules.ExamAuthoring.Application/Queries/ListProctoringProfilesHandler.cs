using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Queries;

/// <summary>Lists the proctoring profiles an author can choose between (FR-46), with what each does and what candidates would be told.</summary>
public sealed class ListProctoringProfilesHandler
{
    /// <summary>Returns every profile, in the order an author sees them, including those not yet available.</summary>
    public IReadOnlyList<ProctoringProfileDto> Handle() => ProctoringProfiles.All.Select(ProctoringMapping.ToDto).ToList();
}
