using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.Infrastructure;

/// <summary>EF Core-backed <see cref="IInviteRepository"/>.</summary>
public sealed class EFInviteRepository(InviteDbContext context) : IInviteRepository
{
    /// <inheritdoc />
    public void Add(InviteAggregate invite) => context.Invites.Add(invite);

    // Tracked, with codes loaded, on purpose: the aggregate's rules read its codes, and handlers rely on
    // change tracking to INSERT new ones; an explicit DbSet.Update would flag them Modified instead.
    /// <inheritdoc />
    public async Task<InviteAggregate?> GetByIdAsync(Guid inviteId, CancellationToken cancellationToken = default) =>
        await context.Invites.Include(i => i.Codes).FirstOrDefaultAsync(i => i.Id == inviteId, cancellationToken);

    /// <inheritdoc />
    public async Task<InviteAggregate> GetByIdOrThrowAsync(Guid inviteId, CancellationToken cancellationToken = default) =>
        await GetByIdAsync(inviteId, cancellationToken) ?? throw new InviteNotFoundError(inviteId);

    /// <inheritdoc />
    public async Task<InviteAggregate?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await context.Invites.Include(i => i.Codes)
            .FirstOrDefaultAsync(i => i.Codes.Any(c => c.Code == code), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InviteAggregate>> ListNewestAsync(int take, CancellationToken cancellationToken = default) =>
        await context.Invites.AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(take).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListAcceptedExamIdsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.Invites.AsNoTracking()
            .Where(i => i.AcceptedByUserId == userId && i.Status == InviteStatus.Accepted)
            .Select(i => i.ExamId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InviteAggregate>> ListAcceptedForExamAsync(Guid examId, CancellationToken cancellationToken = default) =>
        await context.Invites.AsNoTracking()
            .Where(i => i.ExamId == examId && i.Status == InviteStatus.Accepted)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> AnyLiveForExamAsync(Guid examId, CancellationToken cancellationToken = default) =>
        await context.Invites.AsNoTracking()
            .AnyAsync(i => i.ExamId == examId && (i.Status == InviteStatus.Pending || i.Status == InviteStatus.Accepted), cancellationToken);

    /// <inheritdoc />
    public async Task<bool> HasAcceptedAsync(Guid userId, Guid examId, CancellationToken cancellationToken = default) =>
        await context.Invites.AsNoTracking()
            .AnyAsync(i => i.AcceptedByUserId == userId && i.ExamId == examId && i.Status == InviteStatus.Accepted, cancellationToken);
}

/// <summary>EF Core-backed <see cref="IInviteUnitOfWork"/>, wrapping <see cref="InviteDbContext"/>.</summary>
public sealed class InviteUnitOfWork(InviteDbContext context) : IInviteUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
