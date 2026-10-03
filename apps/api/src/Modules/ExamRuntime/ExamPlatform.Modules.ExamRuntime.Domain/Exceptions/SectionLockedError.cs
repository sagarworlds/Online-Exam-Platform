using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>
/// The exam locks sections and the candidate acted on a question outside the section they are in, or tried to go
/// back to a section they already left.
/// </summary>
public sealed class SectionLockedError() : DomainException("This exam locks sections: you cannot return to a section you have left.")
{
    /// <inheritdoc />
    public override string ErrorCode => "section_locked";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
