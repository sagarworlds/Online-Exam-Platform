using ExamPlatform.Modules.Consent.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Consent.Infrastructure;

/// <summary>EF Core-backed <see cref="IConsentUnitOfWork"/>, wrapping <see cref="ConsentDbContext"/>.</summary>
public sealed class ConsentUnitOfWork(ConsentDbContext context) : IConsentUnitOfWork
{
    /// <inheritdoc />
    /// <exception cref="ConcurrencyConflictError">
    /// An incident was changed by another request after this one loaded it, so this save was refused.
    /// </exception>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Translated here so a lost race reaches the caller as the shared 409, not as an EF exception (which would be a 500).
            // The Application layer cannot reference EF Core, and a lost race is something the client resolves by reloading and retrying.
            throw new ConcurrencyConflictError(ex);
        }
    }
}
