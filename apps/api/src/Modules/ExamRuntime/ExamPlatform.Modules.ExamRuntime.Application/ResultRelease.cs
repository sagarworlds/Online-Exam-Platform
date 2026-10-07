using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// When a candidate may see which of their answers were right (FR-12, FR-33), kept in one place so the result page and the
/// review itself can never disagree. One rule covers every mode: released when the mode is Instant, or when the release
/// time has arrived (a manual release just sets that time to the moment the administrator pressed the button).
/// </summary>
public static class ResultRelease
{
    /// <summary>Whether the exam's answers may be shown at <paramref name="nowUtc"/>.</summary>
    /// <param name="exam">The exam.</param>
    /// <param name="nowUtc">The current instant.</param>
    public static bool IsReleased(ExamSnapshot exam, DateTime nowUtc) =>
        exam.ResultRelease == ExamResultReleaseMode.Instant
        || (exam.ResultReleaseTimeUtc is { } releaseAt && nowUtc >= releaseAt);

    /// <summary>
    /// When this attempt's result became visible to its candidate: the moment it was submitted when the exam releases instantly, otherwise
    /// the release time, or the submission if that came later. The start of the dispute window (FR-31).
    /// </summary>
    /// <param name="exam">The exam.</param>
    /// <param name="attempt">The attempt.</param>
    /// <returns>The instant, or <see langword="null"/> while the attempt is open or its answers have no release time yet.</returns>
    public static DateTime? ReleasedAtUtc(ExamSnapshot exam, Attempt attempt)
    {
        if (attempt.SubmittedAtUtc is not { } submittedAt)
            return null;
        if (exam.ResultRelease == ExamResultReleaseMode.Instant)
            return submittedAt;

        return exam.ResultReleaseTimeUtc is { } releaseAt ? (releaseAt > submittedAt ? releaseAt : submittedAt) : null;
    }

    /// <summary>What a candidate is told about the review: whether it is open, how it is decided, and from when if that is known.</summary>
    /// <param name="exam">The exam.</param>
    /// <param name="nowUtc">The current instant.</param>
    public static AttemptReviewAvailabilityDto AvailabilityOf(ExamSnapshot exam, DateTime nowUtc)
    {
        var released = IsReleased(exam, nowUtc);
        return new AttemptReviewAvailabilityDto(released, exam.ResultRelease, released ? null : exam.ResultReleaseTimeUtc);
    }
}
