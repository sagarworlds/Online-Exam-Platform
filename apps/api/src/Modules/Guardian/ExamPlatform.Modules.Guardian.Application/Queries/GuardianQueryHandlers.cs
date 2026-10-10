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

/// <summary>
/// Handles the lookup of a guardian by e-mail address, so staff can link a candidate to the guardian they know by that address. Staff
/// have the address, not the id the platform gave the record.
/// </summary>
public sealed class FindGuardianByEmailHandler(IGuardianRepository repository)
{
    /// <summary>Finds the one guardian registered with the address.</summary>
    /// <param name="email">The address staff typed. Case and surrounding spaces are ignored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The guardian.</returns>
    /// <exception cref="InvalidGuardianDetailsError">The address is missing.</exception>
    /// <exception cref="GuardianNotFoundByEmailError">No guardian is registered with that address.</exception>
    /// <exception cref="GuardianEmailAmbiguousError">Several guardians share that address, so none can be chosen safely.</exception>
    public async Task<GuardianDto> HandleAsync(string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidGuardianDetailsError("Email must be a valid email address.");

        var matches = await repository.ListByEmailAsync(email, cancellationToken);
        if (matches.Count == 0)
            throw new GuardianNotFoundByEmailError();
        if (matches.Count > 1)
            throw new GuardianEmailAmbiguousError();

        return GuardianDto.From(matches[0]);
    }
}
