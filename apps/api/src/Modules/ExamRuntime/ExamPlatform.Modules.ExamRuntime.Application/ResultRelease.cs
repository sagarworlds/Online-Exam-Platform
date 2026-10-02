using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;

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

    /// <summary>What a candidate is told about the review: whether it is open, how it is decided, and from when if that is known.</summary>
    /// <param name="exam">The exam.</param>
    /// <param name="nowUtc">The current instant.</param>
    public static AttemptReviewAvailabilityDto AvailabilityOf(ExamSnapshot exam, DateTime nowUtc)
    {
        var released = IsReleased(exam, nowUtc);
        return new AttemptReviewAvailabilityDto(released, exam.ResultRelease, released ? null : exam.ResultReleaseTimeUtc);
    }
}
