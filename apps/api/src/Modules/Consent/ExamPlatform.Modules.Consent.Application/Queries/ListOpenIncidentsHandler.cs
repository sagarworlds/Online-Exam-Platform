using ExamPlatform.Modules.Consent.Application.Dtos;
using ExamPlatform.Modules.Consent.Application.Ports;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Consent.Application.Queries;

/// <summary>
/// Handles the request for open incidents, which staff use to see what is overdue (FR-52). Escalation happens here, on read:
/// each incident carries its overdue flag as of the moment it is listed. No job runs in the background to set a flag.
/// </summary>
/// <param name="repository">Reads the incidents.</param>
/// <param name="clock">Supplies the instant the overdue flags are worked out at.</param>
public sealed class ListOpenIncidentsHandler(IIncidentRepository repository, Clock clock)
{
    /// <summary>Lists one page of open incidents, oldest escalation due time first.</summary>
    /// <param name="page">The page to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The incidents on the page, each with its overdue flag.</returns>
    public async Task<IReadOnlyList<IncidentDto>> HandleAsync(PageRequest page, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var incidents = await repository.ListOpenAsync(page, cancellationToken);
        return incidents.Select(incident => IncidentDto.From(incident, now)).ToList();
    }
}
