using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Exceptions;

/// <summary>An invite was requested for an exam that does not exist.</summary>
/// <param name="examId">The id that matched no exam.</param>
public sealed class InviteExamNotFoundError(Guid examId) : DomainException($"Exam {examId} does not exist.")
{
    /// <inheritdoc />
    public override string ErrorCode => "exam_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
