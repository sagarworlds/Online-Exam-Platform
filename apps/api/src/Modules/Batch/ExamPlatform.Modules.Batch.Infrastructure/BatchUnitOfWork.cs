using ExamPlatform.Modules.Batch.Application;
using ExamPlatform.Modules.Batch.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExamPlatform.Modules.Batch.Infrastructure;

/// <summary>EF Core-backed <see cref="IBatchUnitOfWork"/>, wrapping <see cref="BatchDbContext"/>.</summary>
public sealed class BatchUnitOfWork(BatchDbContext context) : IBatchUnitOfWork
{
    /// <inheritdoc />
    /// <exception cref="DuplicateMemberError">Another request gave the same address a seat first.</exception>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateActiveMember(ex))
        {
            // The aggregate's own check handles the ordinary case; this is the same rule, decided by the
            // database when two requests raced past it, so the caller still gets a 409 and not a 500.
            throw new DuplicateMemberError();
        }
    }

    private static bool IsDuplicateActiveMember(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: BatchDbContext.ActiveMemberEmailIndexName,
        };
}
