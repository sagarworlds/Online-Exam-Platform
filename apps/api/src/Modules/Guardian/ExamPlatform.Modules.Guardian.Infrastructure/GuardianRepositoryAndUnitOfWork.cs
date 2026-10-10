using Microsoft.EntityFrameworkCore;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;
using ExamPlatform.Modules.Guardian.Application;
using ExamPlatform.Modules.Guardian.Application.Ports;

namespace ExamPlatform.Modules.Guardian.Infrastructure;

public class EFGuardianRepository(GuardianDbContext context) : IGuardianRepository
{
    public void Add(GuardianAggregate guardian) => context.Guardians.Add(guardian);
    // Tracked on purpose: handlers mutate the aggregate and rely on the unit of work's change
    // tracking to INSERT new children; an explicit DbSet.Update would flag them Modified instead.
    public async Task<GuardianAggregate?> GetByIdAsync(Guid guardianId, CancellationToken cancellationToken = default) =>
        await Loaded().FirstOrDefaultAsync(g => g.Id == guardianId, cancellationToken);
    public async Task<GuardianAggregate?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await Loaded().AsNoTracking().FirstOrDefaultAsync(g => g.Email == email, cancellationToken);
    public async Task<IReadOnlyList<GuardianAggregate>> ListByCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default) =>
        await Loaded().AsNoTracking().Where(g => g.CandidateLinks.Any(l => l.CandidateId == candidateId)).ToListAsync(cancellationToken);
    // Tracked, like GetByIdAsync: confirming a link changes it, and the unit of work saves that change.
    public async Task<GuardianAggregate?> GetByVerificationTokenHashAsync(string verificationTokenHash, CancellationToken cancellationToken = default) =>
        await Loaded().FirstOrDefaultAsync(g => g.CandidateLinks.Any(l => l.VerificationTokenHash == verificationTokenHash), cancellationToken);

    // Without its links every rule sees a guardian with none: a second link to the same candidate is
    // accepted, and a revoke finds nothing to revoke.
    private IQueryable<GuardianAggregate> Loaded() => context.Guardians.Include(g => g.CandidateLinks);
}

/// <summary>EF Core-backed <see cref="IGuardianUnitOfWork"/>, wrapping <see cref="GuardianDbContext"/>.</summary>
public sealed class GuardianUnitOfWork(GuardianDbContext context) : IGuardianUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
