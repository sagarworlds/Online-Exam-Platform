using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The candidate has no accommodation at this exam to remove.</summary>
public sealed class AccommodationNotFoundError() : DomainException("This candidate has no accommodation at this exam.")
{
    /// <inheritdoc />
    public override string ErrorCode => "accommodation_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
