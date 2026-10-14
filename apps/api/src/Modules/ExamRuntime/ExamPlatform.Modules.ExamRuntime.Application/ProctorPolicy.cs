using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// The platform's proctoring policy. It applies the accommodation rules (FR-49) to the exam as authored. The minors' default (requirements
/// section 7.2) is not applied here yet: it governs camera and screen capture, which no exam can select until those profiles are built,
/// and <c>ProctoringProfiles.ForCandidate</c> in the authoring module is the rule it will call then.
/// </summary>
public sealed class ProctorPolicy : IProctorPolicy
{
    /// <inheritdoc />
    public ExamSnapshot Resolve(ExamSnapshot exam, IEnumerable<string> alternateFormats) => AccommodationPolicy.Apply(exam, alternateFormats);
}
