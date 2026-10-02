using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure;

/// <summary>EF Core-backed <see cref="IExamRuntimeUnitOfWork"/>, wrapping <see cref="ExamRuntimeDbContext"/>.</summary>
public sealed class ExamRuntimeUnitOfWork(ExamRuntimeDbContext context) : IExamRuntimeUnitOfWork
{
    /// <summary>Postgres's SQLSTATE for a unique-constraint violation.</summary>
    private const string UniqueViolation = "23505";

    /// <inheritdoc />
    /// <exception cref="ConcurrencyConflictError">
    /// Another request changed the same attempt first, started the same attempt, saved the same answer, or granted the same
    /// extra attempt at the same moment; the caller reloads and carries on from what is stored.
    /// </exception>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictError(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Only the unique indexes on (exam, candidate, attempt number), (attempt, question) and (exam, candidate,
            // grant number) can raise this here, and all mean "another request got there first", an expected race and not
            // a server fault.
            throw new ConcurrencyConflictError(ex);
        }
    }
}
