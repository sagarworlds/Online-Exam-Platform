using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure;

/// <summary>EF Core-backed <see cref="IExamRuntimeUnitOfWork"/>, wrapping <see cref="ExamRuntimeDbContext"/>.</summary>
public sealed class ExamRuntimeUnitOfWork(ExamRuntimeDbContext context) : IExamRuntimeUnitOfWork
{
    /// <summary>Postgres's SQLSTATE for a unique-constraint violation.</summary>
    private const string UniqueViolation = "23505";

    /// <inheritdoc />
    public async Task<IAttemptLock> LockAttemptAsync(Guid attemptId, bool exclusive, CancellationToken cancellationToken)
    {
        // A transaction of its own: the row lock lives as long as it does, and the saves in between join it.
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // FOR UPDATE waits for every holder and blocks every other; FOR SHARE only conflicts with FOR UPDATE, so many answers
            // can be saved together. The statements differ because the lock strength cannot be a parameter.
            if (exclusive)
                await context.Database.ExecuteSqlAsync($"""SELECT 1 FROM "examRuntime"."Attempts" WHERE "Id" = {attemptId} FOR UPDATE""", cancellationToken);
            else
                await context.Database.ExecuteSqlAsync($"""SELECT 1 FROM "examRuntime"."Attempts" WHERE "Id" = {attemptId} FOR SHARE""", cancellationToken);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }

        return new AttemptLock(transaction);
    }

    private sealed class AttemptLock(IDbContextTransaction transaction) : IAttemptLock
    {
        public Task CompleteAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

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
