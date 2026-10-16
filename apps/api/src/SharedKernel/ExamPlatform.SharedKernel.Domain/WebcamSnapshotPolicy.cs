namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// The fixed rules of webcam snapshots (FR-24). They live in the shared kernel because the authoring notice states the interval to the
/// candidate, and the runtime enforces the same interval and size limits; no module may reference another module's domain.
/// </summary>
public static class WebcamSnapshotPolicy
{
    /// <summary>How often the exam page takes a photo, in seconds. The notice says this number, so it is never a guess.</summary>
    public const int IntervalSeconds = 120;

    /// <summary>The largest photo the server accepts, in bytes. A small JPEG is enough to see who is at the desk; a larger upload is refused.</summary>
    public const int MaxImageBytes = 200 * 1024;

    /// <summary>The only image type the server accepts, so a stored photo is always something a browser can show without a surprise.</summary>
    public const string ContentType = "image/jpeg";

    /// <summary>
    /// The most photos kept for one attempt. At <see cref="IntervalSeconds"/> this covers more than 16 hours, so it only stops a client that
    /// uploads far faster than the interval from filling the database.
    /// </summary>
    public const int MaxPerAttempt = 500;

    /// <summary>The fewest seconds between two photos of one attempt. A little under the interval, to allow for a slow network batching two.</summary>
    public const int MinGapSeconds = IntervalSeconds / 2;
}
