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
        await context.PasswordResetTokens
            .Where(t => t.UserId == userId
                && t.ConsumedAtUtc == null
                && t.RevokedAtUtc == null
                && t.ExpiresAtUtc >= nowUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken) =>
        await context.PasswordResetTokens.AddAsync(token, cancellationToken);
}
