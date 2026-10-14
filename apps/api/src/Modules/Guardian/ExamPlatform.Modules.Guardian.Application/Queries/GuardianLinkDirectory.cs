using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Contracts;

namespace ExamPlatform.Modules.Guardian.Application.Queries;

/// <summary>The <see cref="IGuardianLinkDirectory"/> other modules ask whether a candidate has a confirmed guardian.</summary>
public sealed class GuardianLinkDirectory(IGuardianRepository repository) : IGuardianLinkDirectory
{
    /// <inheritdoc />
    public async Task<bool> HasConfirmedGuardianAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        // The repository loads each guardian with all their links, so the candidate's own links are checked here. A verified link is
        // one that was confirmed and not revoked or removed; a revoked link has a different status, so it is not verified.
        var guardians = await repository.ListByCandidateAsync(candidateId, cancellationToken);
        return guardians.Any(guardian => guardian.CandidateLinks.Any(link => link.CandidateId == candidateId && link.IsVerified()));
    }
}
