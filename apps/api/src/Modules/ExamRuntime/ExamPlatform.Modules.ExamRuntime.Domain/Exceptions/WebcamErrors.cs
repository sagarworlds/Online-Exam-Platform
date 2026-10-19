using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>Nothing of this kind is captured in this attempt: the exam does not ask for it, or the candidate is under 18 (FR-24, FR-25).</summary>
public sealed class WebcamNotRequiredError() : DomainException("This is not captured in this attempt.")
{
    /// <inheritdoc />
    public override string ErrorCode => "webcam_not_required";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}

/// <summary>A photo arrived sooner after the last one than the interval allows, so it was not stored.</summary>
public sealed class WebcamPhotoTooSoonError() : DomainException("A photo was taken a moment ago. Wait for the next interval.")
{
    /// <inheritdoc />
    public override string ErrorCode => "webcam_photo_too_soon";

    /// <inheritdoc />
    public override int HttpStatusCode => 429;
}

/// <summary>The attempt already holds the most photos it may.</summary>
public sealed class WebcamPhotoLimitError() : DomainException("This attempt already holds the most webcam photos it can.")
{
    /// <inheritdoc />
    public override string ErrorCode => "webcam_photo_limit";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}

/// <summary>No such photo in the attempt.</summary>
public sealed class WebcamPhotoNotFoundError() : DomainException("There is no such photo.")
{
    /// <inheritdoc />
    public override string ErrorCode => "webcam_photo_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
