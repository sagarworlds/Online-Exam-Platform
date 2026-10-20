using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>Tells the exam page whether to take webcam photos (FR-24) and screen pictures (FR-25) in an attempt, and how often.</summary>
public sealed class GetWebcamPolicyHandler(AttemptAccess access, WebcamEligibility eligibility)
{
    /// <summary>The policy for one of the candidate's own attempts. A finished attempt takes no more photos.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    public async Task<WebcamPolicyDto> HandleAsync(Guid attemptId, Guid candidateId, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        var open = attempt.Status == AttemptStatus.InProgress;
        var photos = open && await eligibility.AppliesAsync(attempt, exam, CaptureKind.Webcam, cancellationToken);
        var screen = open && await eligibility.AppliesAsync(attempt, exam, CaptureKind.Screen, cancellationToken);

        // Screen pictures are taken at the same interval as photos.
        return new WebcamPolicyDto(
            photos, photos ? WebcamSnapshotPolicy.IntervalSeconds : 0, screen, screen ? WebcamSnapshotPolicy.IntervalSeconds : 0);
    }
}

/// <summary>Stores a webcam photo (FR-24) or screen picture (FR-25) the exam page took.</summary>
public sealed class RecordWebcamPhotoHandler(
    AttemptAccess access, WebcamEligibility eligibility, IWebcamPhotoRepository photos, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Stores one capture, if the attempt is open, captures of that kind are taken in it, and the interval and the limit allow it.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="image">The JPEG bytes the page sent.</param>
    /// <param name="kind">Whether the image is a webcam photo or a screen picture; each has its own setting, spacing and limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted, or its time has just run out and it was closed.</exception>
    /// <exception cref="WebcamNotRequiredError">The exam takes no captures of that kind, or the candidate is under 18.</exception>
    /// <exception cref="InvalidAttemptError">The image is empty, too large or not a JPEG.</exception>
    /// <exception cref="WebcamPhotoTooSoonError">The last photo arrived less than the minimum gap ago.</exception>
    /// <exception cref="WebcamPhotoLimitError">The attempt already holds the most photos it may.</exception>
    public async Task HandleAsync(Guid attemptId, Guid candidateId, byte[] image, CaptureKind kind, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        if (attempt.Status != AttemptStatus.InProgress)
            throw new AttemptNotInProgressError();

        // Checked before the image is looked at, so a candidate who is not photographed learns that without sending anything useful.
        if (!await eligibility.AppliesAsync(attempt, exam, kind, cancellationToken))
            throw new WebcamNotRequiredError();

        var now = clock.UtcNow;
        var latest = await photos.LatestCapturedAtAsync(attemptId, kind, cancellationToken);
        if (latest is not null && (now - latest.Value).TotalSeconds < WebcamSnapshotPolicy.MinGapSeconds)
            throw new WebcamPhotoTooSoonError();

        if (await photos.CountAsync(attemptId, kind, cancellationToken) >= WebcamSnapshotPolicy.MaxPerAttempt)
            throw new WebcamPhotoLimitError();

        photos.Add(AttemptWebcamPhoto.Create(attemptId, image, now, kind));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Lets staff list the webcam photos of an attempt, without reading the images (FR-24).</summary>
public sealed class ListWebcamPhotosHandler(IAttemptRepository attempts, IWebcamPhotoRepository photos)
{
    /// <summary>Lists the photos of one attempt of an exam, oldest first.</summary>
    /// <param name="examId">The exam the attempt belongs to; an attempt of another exam is reported as not found.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt in that exam.</exception>
    public async Task<IReadOnlyList<WebcamPhotoSummary>> HandleAsync(Guid examId, Guid attemptId, CancellationToken cancellationToken)
    {
        var attempt = await attempts.GetByIdAsync(attemptId, cancellationToken);
        if (attempt is null || attempt.ExamId != examId)
            throw new AttemptNotFoundError();

        return await photos.ListAsync(attemptId, cancellationToken);
    }
}

/// <summary>Lets staff read one webcam photo of an attempt (FR-24).</summary>
public sealed class GetWebcamPhotoHandler(IAttemptRepository attempts, IWebcamPhotoRepository photos)
{
    /// <summary>Loads one photo.</summary>
    /// <param name="examId">The exam the attempt belongs to; an attempt of another exam is reported as not found.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="photoId">The photo.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt in that exam.</exception>
    /// <exception cref="WebcamPhotoNotFoundError">The attempt has no such photo.</exception>
    public async Task<AttemptWebcamPhoto> HandleAsync(Guid examId, Guid attemptId, Guid photoId, CancellationToken cancellationToken)
    {
        var attempt = await attempts.GetByIdAsync(attemptId, cancellationToken);
        if (attempt is null || attempt.ExamId != examId)
            throw new AttemptNotFoundError();

        return await photos.GetAsync(attemptId, photoId, cancellationToken) ?? throw new WebcamPhotoNotFoundError();
    }
}
