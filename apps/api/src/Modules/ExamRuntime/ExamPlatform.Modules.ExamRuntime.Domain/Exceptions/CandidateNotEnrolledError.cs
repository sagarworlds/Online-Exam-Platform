using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The person is not a candidate of this exam: they never accepted an invitation to it.</summary>
public sealed class CandidateNotEnrolledError() : DomainException("This person is not enrolled in the exam.")
{
    /// <inheritdoc />
    public override string ErrorCode => "candidate_not_enrolled";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
