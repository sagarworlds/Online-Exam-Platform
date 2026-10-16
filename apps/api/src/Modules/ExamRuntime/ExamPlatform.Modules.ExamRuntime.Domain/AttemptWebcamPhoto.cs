using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// One webcam photo taken of a candidate while they sat an <see cref="Attempt"/> (FR-24). Kept as its own row rather than inside the
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

    /// <summary>When the server received it.</summary>
    public DateTime CapturedAtUtc { get; private set; }

    /// <summary>The JPEG bytes.</summary>
    public byte[] Image { get; private set; } = [];

    // For EF Core.
    private AttemptWebcamPhoto() : base(Guid.Empty)
    {
    }

    private AttemptWebcamPhoto(Guid attemptId, byte[] image, DateTime capturedAtUtc) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        Image = image;
        CapturedAtUtc = capturedAtUtc;
    }

    /// <summary>Makes a photo from what the browser sent, after checking it is a JPEG of an acceptable size.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="image">The bytes received.</param>
    /// <param name="capturedAtUtc">The server's current instant.</param>
    /// <exception cref="InvalidAttemptError">The image is empty, larger than <see cref="WebcamSnapshotPolicy.MaxImageBytes"/>, or not a JPEG.</exception>
    public static AttemptWebcamPhoto Create(Guid attemptId, byte[] image, DateTime capturedAtUtc)
    {
        if (image.Length == 0)
            throw new InvalidAttemptError("The photo is empty.");
        if (image.Length > WebcamSnapshotPolicy.MaxImageBytes)
            throw new InvalidAttemptError($"The photo is larger than {WebcamSnapshotPolicy.MaxImageBytes / 1024} KB.");
        if (image.Length < JpegMagic.Length || !image.AsSpan(0, JpegMagic.Length).SequenceEqual(JpegMagic))
            throw new InvalidAttemptError("The photo must be a JPEG.");

        return new AttemptWebcamPhoto(attemptId, image, capturedAtUtc);
    }
}
