using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Contracts;

namespace ExamPlatform.Modules.Invite.Application;

/// <summary>The <see cref="IEnrollments"/> other modules read enrollment through: accepting an invite enrolls the accepter.</summary>
public sealed class EnrollmentReader(IInviteRepository repository) : IEnrollments
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> GetEnrolledExamIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        repository.ListAcceptedExamIdsAsync(userId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsEnrolledAsync(Guid userId, Guid examId, CancellationToken cancellationToken) =>
        repository.HasAcceptedAsync(userId, examId, cancellationToken);
}
