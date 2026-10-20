using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>Persistence port for <see cref="AttemptWebcamPhoto"/>.</summary>
public interface IWebcamPhotoRepository
{
    /// <summary>Starts tracking a new photo; it is stored when the unit of work saves.</summary>
    /// <param name="photo">The photo to add.</param>
    void Add(AttemptWebcamPhoto photo);

    /// <summary>Counts the captures of one kind held for one attempt.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="kind">Webcam photos or screen pictures; each has its own limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountAsync(Guid attemptId, CaptureKind kind, CancellationToken cancellationToken);

    /// <summary>When the latest capture of that kind in the attempt arrived, or null when there is none.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="kind">Webcam photos or screen pictures; each has its own spacing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DateTime?> LatestCapturedAtAsync(Guid attemptId, CaptureKind kind, CancellationToken cancellationToken);

    /// <summary>Lists the attempt's photo ids and times, oldest first, without reading the images.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<WebcamPhotoSummary>> ListAsync(Guid attemptId, CancellationToken cancellationToken);

    /// <summary>Loads one photo of an attempt.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="photoId">The photo.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The photo, or null when the attempt has none with that id.</returns>
    Task<AttemptWebcamPhoto?> GetAsync(Guid attemptId, Guid photoId, CancellationToken cancellationToken);
}

/// <summary>A capture as a list shows it: what it shows, when it was taken and how big it is.</summary>
/// <param name="Id">The capture's id.</param>
/// <param name="Kind">Whether it shows the candidate or their screen.</param>
/// <param name="CapturedAtUtc">When the server received it.</param>
/// <param name="SizeBytes">The image size.</param>
public sealed record WebcamPhotoSummary(Guid Id, CaptureKind Kind, DateTime CapturedAtUtc, int SizeBytes);
