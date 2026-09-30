using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.Application.Ports;

/// Repository port for Invite aggregate.
public interface IInviteRepository
{
    void Add(InviteAggregate invite);
    Task<InviteAggregate?> GetByIdAsync(Guid inviteId, CancellationToken cancellationToken = default);
    Task<InviteAggregate> GetByIdOrThrowAsync(Guid inviteId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InviteAggregate>> ListByBatchMemberAsync(Guid batchMemberId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InviteAggregate>> ListByExamAsync(Guid examId, CancellationToken cancellationToken = default);
}
