using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Identity.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IPasswordResetTokenRepository"/>.</summary>
public sealed class PasswordResetTokenRepository(IdentityDbContext context) : IPasswordResetTokenRepository
{
    /// <inheritdoc />
    public Task<PasswordResetToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.PasswordResetTokens.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PasswordResetToken>> GetOutstandingForUserAsync(
        Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        await Outstanding(userId, nowUtc).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> RevokeOutstandingForUserAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        // One UPDATE ... WHERE: Postgres re-checks the filter on a row that a parallel
        // transaction changed meanwhile, so a token it already revoked or consumed is skipped
        // rather than overwritten (the same no-op rule as PasswordResetToken.Revoke). The
        // update still moves the row's xmin, so a reset that loaded the token before it was
        // revoked fails its own save with a 409 instead of using the revoked link.
        Outstanding(userId, nowUtc).ExecuteUpdateAsync(
            setters => setters.SetProperty(t => t.RevokedAtUtc, nowUtc), cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken) =>
        await context.PasswordResetTokens.AddAsync(token, cancellationToken);

    // The tokens IsUsable would still accept; ">=" matches its inclusive expiry bound.
    private IQueryable<PasswordResetToken> Outstanding(Guid userId, DateTime nowUtc) =>
        context.PasswordResetTokens.Where(t => t.UserId == userId
            && t.ConsumedAtUtc == null
            && t.RevokedAtUtc == null
            && t.ExpiresAtUtc >= nowUtc);
}
