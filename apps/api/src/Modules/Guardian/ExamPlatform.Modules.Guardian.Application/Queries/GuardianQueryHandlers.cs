using ExamPlatform.Modules.Guardian.Application.Dtos;
using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Domain.Exceptions;

namespace ExamPlatform.Modules.Guardian.Application.Queries;

/// <summary>Handles the request for a guardian's candidate links, for the guardian dashboard (FR-22).</summary>
public sealed class ListGuardianLinksHandler(IGuardianRepository repository)
{
    /// <summary>
    /// Lists the links from a guardian to candidates. A revoked link is included, because the dashboard shows
    /// its status. An unlinked one is left out, since the link was removed rather than revoked.
    /// </summary>
    /// <param name="guardianId">The guardian whose links to list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GuardianNotFoundError">No guardian has that id.</exception>
    public async Task<IReadOnlyList<GuardianLinkDto>> HandleAsync(Guid guardianId, CancellationToken cancellationToken)
    {
        var guardian = await repository.GetByIdAsync(guardianId, cancellationToken)
            ?? throw new GuardianNotFoundError(guardianId);

        return guardian.CandidateLinks
            .Where(link => !link.IsDeleted)
            .Select(GuardianLinkDto.From)
            .ToList();
    }
}
