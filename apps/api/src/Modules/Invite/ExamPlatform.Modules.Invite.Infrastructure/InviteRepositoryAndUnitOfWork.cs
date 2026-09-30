using Microsoft.EntityFrameworkCore;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;
using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Ports;

namespace ExamPlatform.Modules.Invite.Infrastructure;

public class EFInviteRepository(InviteDbContext context) : IInviteRepository
{
    public void Add(InviteAggregate invite) => context.Invites.Add(invite);
    // Tracked, with children loaded, on purpose: the aggregate's rules (duplicate/capacity/
    // validity checks) read its child collection, and handlers rely on change tracking to
    // INSERT new children; an explicit DbSet.Update would flag them Modified instead.
    public async Task<InviteAggregate?> GetByIdAsync(Guid inviteId, CancellationToken cancellationToken = default) =>
        await context.Invites.Include(i => i.Codes).FirstOrDefaultAsync(i => i.Id == inviteId, cancellationToken);
    public async Task<InviteAggregate> GetByIdOrThrowAsync(Guid inviteId, CancellationToken cancellationToken = default)
    {
        var invite = await GetByIdAsync(inviteId, cancellationToken);
        if (invite == null) throw new InvalidOperationException($"Invite with ID {inviteId} not found.");
        return invite;
    }
    public async Task<IReadOnlyList<InviteAggregate>> ListByBatchMemberAsync(Guid batchMemberId, CancellationToken cancellationToken = default) =>
        await context.Invites.AsNoTracking().Where(i => i.BatchMemberId == batchMemberId).ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<InviteAggregate>> ListByExamAsync(Guid examId, CancellationToken cancellationToken = default) =>
        await context.Invites.AsNoTracking().Where(i => i.ExamId == examId).ToListAsync(cancellationToken);
}

public class InviteUnitOfWork(InviteDbContext context) : IInviteUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await context.SaveChangesAsync(cancellationToken);
}
