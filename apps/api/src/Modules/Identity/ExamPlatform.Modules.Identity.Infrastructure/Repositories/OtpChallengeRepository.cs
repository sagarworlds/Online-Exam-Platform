using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Identity.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IOtpChallengeRepository"/>.</summary>
public sealed class OtpChallengeRepository(IdentityDbContext context) : IOtpChallengeRepository
{
    /// <inheritdoc />
    public Task<OtpChallenge?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.OtpChallenges.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OtpChallenge>> GetOutstandingAsync(
        string destination, OtpPurpose purpose, DateTime nowUtc, CancellationToken cancellationToken) =>
        await context.OtpChallenges
            .Where(c => c.Destination == destination
                && c.Purpose == purpose
                && c.ConsumedAtUtc == null
                && c.SupersededAtUtc == null
                && c.ExpiresAtUtc >= nowUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(OtpChallenge challenge, CancellationToken cancellationToken) =>
        await context.OtpChallenges.AddAsync(challenge, cancellationToken);
}
