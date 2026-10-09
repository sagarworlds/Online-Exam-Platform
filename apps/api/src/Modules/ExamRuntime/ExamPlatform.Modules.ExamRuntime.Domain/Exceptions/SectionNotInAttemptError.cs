using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>A candidate asked to move to a section that is not part of the exam they are sitting.</summary>
public sealed class SectionNotInAttemptError() : DomainException("That section is not part of this exam.")
{
    /// <inheritdoc />
    public override string ErrorCode => "section_not_in_attempt";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
