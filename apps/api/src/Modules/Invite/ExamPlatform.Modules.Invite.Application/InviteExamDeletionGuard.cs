using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.Invite.Application.Ports;

namespace ExamPlatform.Modules.Invite.Application;

/// <summary>
/// Objects to an exam being deleted while someone has been invited to it. An invitation holds the exam's id; deleting the
/// exam would leave a candidate with a link to an exam that no longer exists. Revoke the invitations first.
/// </summary>
public sealed class InviteExamDeletionGuard(IInviteRepository repository) : IExamDeletionGuard
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindObjectionsAsync(Guid examId, CancellationToken cancellationToken) =>
        await repository.AnyLiveForExamAsync(examId, cancellationToken)
            ? ["Candidates have been invited to it; revoke the invitations first."]
            : [];
}
