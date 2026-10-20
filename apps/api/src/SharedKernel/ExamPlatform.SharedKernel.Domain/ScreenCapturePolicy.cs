namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// The fixed rules of screen capture (FR-25). It takes pictures at the same interval and under the same per-attempt and spacing limits as
/// webcam snapshots (<see cref="WebcamSnapshotPolicy"/>), so those are not repeated here; only the size differs, because a picture of a
/// screen needs more detail than a photo of a face to be worth reading.
/// </summary>
public static class ScreenCapturePolicy
{
    /// <summary>The largest screen picture the server accepts, in bytes.</summary>
    public const int MaxImageBytes = 500 * 1024;
}
