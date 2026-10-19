using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.Identity.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Decides whether webcam photos (FR-24) or screen pictures (FR-25) are taken in one attempt. This is the one place that combines the exam's setting with the minors'
/// default (requirements section 7.2), so counsel's answer on issue #11 changes it here alone.
/// </summary>
public sealed class WebcamEligibility(ICandidateAgeDirectory ages)
{
    /// <summary>
    /// Whether captures of this kind are taken: the exam asks for them and the candidate was an adult when the attempt started. A candidate
    /// whose age cannot be shown counts as a minor (see <see cref="ICandidateAgeDirectory"/>), so the rule fails safe.
    /// </summary>
    /// <param name="attempt">The attempt.</param>
    /// <param name="exam">Its exam.</param>
    /// <param name="kind">Webcam photos (FR-24) or screen pictures (FR-25).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> AppliesAsync(Attempt attempt, ExamSnapshot exam, CaptureKind kind, CancellationToken cancellationToken)
    {
        var asked = kind == CaptureKind.Screen ? exam.ScreenCapture : exam.WebcamSnapshots;
        if (!asked)
            return false;

        return !await ages.IsMinorOnAsync(attempt.CandidateId, attempt.StartedAtUtc, cancellationToken);
    }
}
