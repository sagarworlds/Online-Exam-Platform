using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>EF Core-backed <see cref="IIdentityUnitOfWork"/>, wrapping <see cref="IdentityDbContext"/>.</summary>
public sealed class IdentityUnitOfWork(IdentityDbContext context) : IIdentityUnitOfWork
{
    /// <inheritdoc />
    /// <exception cref="ConcurrencyConflictError">
    /// A row-versioned entity (e.g. an OTP challenge) was changed by another request after this one loaded it.
    /// </exception>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Translated here so callers see a typed 409 instead of an EF exception (and a
            // 500): the Application layer cannot reference EF Core, and a lost race is an
            // expected outcome the client resolves by retrying, not a server fault.
            throw new ConcurrencyConflictError(ex);
        }
    }
}
