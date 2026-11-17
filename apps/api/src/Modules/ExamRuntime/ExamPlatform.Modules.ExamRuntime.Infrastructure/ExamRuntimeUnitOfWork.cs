using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.SharedKernel.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure;

/// <summary>EF Core-backed <see cref="IExamRuntimeUnitOfWork"/>, wrapping <see cref="ExamRuntimeDbContext"/>.</summary>
public sealed class ExamRuntimeUnitOfWork(ExamRuntimeDbContext context, GradingWakeUp gradingWakeUp) : IExamRuntimeUnitOfWork
{
    /// <summary>Postgres's SQLSTATE for a unique-constraint violation.</summary>
    private const string UniqueViolation = "23505";

    // Typed answers were queued inside a transaction that has not committed yet: the grading worker is nudged when it does, not before, or it
    // would look, find nothing, and go back to sleep with the answers still waiting.
    private bool _wakeWorkerAfterCommit;

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

        return new AttemptLock(transaction, this);
    }

    private sealed class AttemptLock(IDbContextTransaction transaction, ExamRuntimeUnitOfWork owner) : IAttemptLock
    {
        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            await transaction.CommitAsync(cancellationToken);
            owner.WakeWorkerIfAnswersWereQueued();
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    // Typed answers newly queued for the AI evaluator in this save. A worker saving a verdict or a failure changes the same rows but not when they
    // were queued, so it never wakes itself, and a failed answer waits out its backoff instead of being tried again at once.
    private bool QueuesAnswersForEvaluation() =>
        context.ChangeTracker.Entries<AttemptAnswer>().Any(entry =>
            entry.Entity.EvaluationRequestedAtUtc is not null
            && (entry.State == EntityState.Added
                || (entry.State == EntityState.Modified && entry.Property(answer => answer.EvaluationRequestedAtUtc).IsModified)));

    private void WakeWorkerIfAnswersWereQueued()
    {
        if (!_wakeWorkerAfterCommit)
            return;

        _wakeWorkerAfterCommit = false;
        gradingWakeUp.Notify();
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
            // Asked before the save, which resets what the change tracker knows about each row.
            var queuesAnswers = QueuesAnswersForEvaluation();
            var saved = await context.SaveChangesAsync(cancellationToken);
            if (queuesAnswers)
            {
                // Inside a transaction the answers are not visible to the worker until it commits (see LockAttemptAsync), so it is nudged then.
                if (context.Database.CurrentTransaction is null)
                    gradingWakeUp.Notify();
                else
                    _wakeWorkerAfterCommit = true;
            }

            return saved;
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
