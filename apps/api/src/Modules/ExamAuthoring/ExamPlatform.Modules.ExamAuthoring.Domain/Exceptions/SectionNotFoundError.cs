using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>The exam has no section with the requested id.</summary>
/// <param name="sectionId">The id that matched nothing.</param>
public sealed class SectionNotFoundError(Guid sectionId) : DomainException($"Section {sectionId} was not found in this exam.")
{
    /// <inheritdoc />
    public override string ErrorCode => "section_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
