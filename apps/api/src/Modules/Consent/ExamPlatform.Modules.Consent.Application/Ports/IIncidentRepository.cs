using ExamPlatform.Modules.Consent.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Consent.Application.Ports;

/// <summary>Persistence port for <see cref="Incident"/> aggregates (FR-52).</summary>
public interface IIncidentRepository
{
    /// <summary>Loads an incident with its full status history, or null if none exists.</summary>
    /// <param name="id">The incident's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Incident?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Loads one page of open incidents, meaning every incident not yet closed, with their status history. The order is the
    /// escalation due time, oldest first, with the id breaking ties so that pages do not shift between requests.
    /// </summary>
    /// <param name="page">The page to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Incident>> ListOpenAsync(PageRequest page, CancellationToken cancellationToken);

    /// <summary>Begins tracking a new incident for insertion on the next unit-of-work commit.</summary>
    /// <param name="incident">The incident to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(Incident incident, CancellationToken cancellationToken);
}
