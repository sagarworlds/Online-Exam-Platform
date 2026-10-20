using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// The platform's proctoring policy. It applies the accommodation rules (FR-49) to the exam as authored. The minors' default (requirements
/// section 7.2) is not applied here: it governs camera and screen capture, and <see cref="WebcamEligibility"/> is the one place that
/// decides it, per attempt, from the candidate's age when the attempt started. <see cref="Resolve"/> has no candidate age to work with, so
/// the rule is deliberately not duplicated here.
/// </summary>
public sealed class ProctorPolicy : IProctorPolicy
{
    /// <inheritdoc />
    public ExamSnapshot Resolve(ExamSnapshot exam, IEnumerable<string> alternateFormats) => AccommodationPolicy.Apply(exam, alternateFormats);
}
