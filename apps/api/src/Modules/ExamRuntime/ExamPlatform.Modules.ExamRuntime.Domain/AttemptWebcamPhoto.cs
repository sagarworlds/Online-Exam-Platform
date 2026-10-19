using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// One webcam photo (FR-24) or screen picture (FR-25) taken while a candidate sat an <see cref="Attempt"/>. The two share a table because they are
/// stored, listed, limited and read the same way; <see cref="Kind"/> tells them apart. Kept as its own row rather than inside the
/// attempt, so loading an attempt never loads its photos, and so a retention job can remove photos without touching the attempt.
/// </summary>
/// <remarks>
/// The time is the server's, taken when the photo arrived, never the browser's. The photo is evidence for a human reviewer to look at;
/// nothing here reads its pixels, and nothing treats a missing photo as a verdict (a camera can be covered or a request lost).
/// </remarks>
public sealed class AttemptWebcamPhoto : Entity
{
    // A JPEG always starts with these three bytes. Checking them is not a proof the file is a picture, but it stops a client storing
    // arbitrary content under the image type, which is what a reviewer's browser would then be asked to open.
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    /// <summary>The attempt this belongs to.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>Whether it shows the candidate or their screen.</summary>
    public CaptureKind Kind { get; private set; }

    /// <summary>When the server received it.</summary>
    public DateTime CapturedAtUtc { get; private set; }

    /// <summary>The JPEG bytes.</summary>
    public byte[] Image { get; private set; } = [];

    // For EF Core.
    private AttemptWebcamPhoto() : base(Guid.Empty)
    {
    }

    private AttemptWebcamPhoto(Guid attemptId, CaptureKind kind, byte[] image, DateTime capturedAtUtc) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        Kind = kind;
        Image = image;
        CapturedAtUtc = capturedAtUtc;
    }

    /// <summary>Makes a photo from what the browser sent, after checking it is a JPEG of an acceptable size.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="image">The bytes received.</param>
    /// <param name="capturedAtUtc">The server's current instant.</param>
    /// <param name="kind">Whether the image shows the candidate or their screen; webcam unless said otherwise.</param>
    /// <exception cref="InvalidAttemptError">The image is empty, larger than the limit for its kind, or not a JPEG.</exception>
    public static AttemptWebcamPhoto Create(Guid attemptId, byte[] image, DateTime capturedAtUtc, CaptureKind kind = CaptureKind.Webcam)
    {
        var maxBytes = MaxBytesFor(kind);
        if (image.Length == 0)
            throw new InvalidAttemptError("The image is empty.");
        if (image.Length > maxBytes)
            throw new InvalidAttemptError($"The image is larger than {maxBytes / 1024} KB.");
        if (image.Length < JpegMagic.Length || !image.AsSpan(0, JpegMagic.Length).SequenceEqual(JpegMagic))
            throw new InvalidAttemptError("The image must be a JPEG.");

        return new AttemptWebcamPhoto(attemptId, kind, image, capturedAtUtc);
    }

    /// <summary>The largest image the server accepts for a kind of capture.</summary>
    /// <param name="kind">The kind of capture.</param>
    public static int MaxBytesFor(CaptureKind kind) =>
        kind == CaptureKind.Screen ? ScreenCapturePolicy.MaxImageBytes : WebcamSnapshotPolicy.MaxImageBytes;
}
