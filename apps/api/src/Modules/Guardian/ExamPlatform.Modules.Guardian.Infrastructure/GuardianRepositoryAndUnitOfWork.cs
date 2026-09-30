using Microsoft.EntityFrameworkCore;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;
using ExamPlatform.Modules.Guardian.Application;
using ExamPlatform.Modules.Guardian.Application.Ports;

namespace ExamPlatform.Modules.Guardian.Infrastructure;

public class EFGuardianRepository(GuardianDbContext context) : IGuardianRepository
{
    public void Add(GuardianAggregate guardian) => context.Guardians.Add(guardian);
    // Tracked, with children loaded, on purpose: the aggregate's rules (duplicate/capacity/
    // validity checks) read its child collection, and handlers rely on change tracking to
    // INSERT new children; an explicit DbSet.Update would flag them Modified instead.
    public async Task<GuardianAggregate?> GetByIdAsync(Guid guardianId, CancellationToken cancellationToken = default) =>
        await context.Guardians.Include(g => g.CandidateLinks).FirstOrDefaultAsync(g => g.Id == guardianId, cancellationToken);
    public async Task<GuardianAggregate> GetByIdOrThrowAsync(Guid guardianId, CancellationToken cancellationToken = default)
    {
        var guardian = await GetByIdAsync(guardianId, cancellationToken);
        if (guardian == null) throw new InvalidOperationException($"Guardian with ID {guardianId} not found.");
        return guardian;
    }
    public async Task<GuardianAggregate?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await context.Guardians.AsNoTracking().FirstOrDefaultAsync(g => g.Email == email, cancellationToken);
    public async Task<IReadOnlyList<GuardianAggregate>> ListByCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default) =>
        await context.Guardians.AsNoTracking().Where(g => g.CandidateLinks.Any(l => l.CandidateId == candidateId)).ToListAsync(cancellationToken);
}

public class GuardianUnitOfWork(GuardianDbContext context) : IGuardianUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await context.SaveChangesAsync(cancellationToken);
}
