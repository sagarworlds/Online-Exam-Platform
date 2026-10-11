using ExamPlatform.Modules.Consent.Application.Ports;
using ExamPlatform.Modules.Consent.Domain;
using ExamPlatform.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Consent.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IIncidentRepository"/>.</summary>
public sealed class IncidentRepository(ConsentDbContext context) : IIncidentRepository
{
    /// <inheritdoc />
    public Task<Incident?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Incidents
            .Include(i => i.StatusChanges)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Incident>> ListOpenAsync(PageRequest page, CancellationToken cancellationToken) =>
        await context.Incidents
            .Where(i => i.Status != IncidentStatus.Closed)
            .OrderBy(i => i.EscalationDueAtUtc)
            .ThenBy(i => i.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Include(i => i.StatusChanges)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Incident incident, CancellationToken cancellationToken) =>
        await context.Incidents.AddAsync(incident, cancellationToken);
}
