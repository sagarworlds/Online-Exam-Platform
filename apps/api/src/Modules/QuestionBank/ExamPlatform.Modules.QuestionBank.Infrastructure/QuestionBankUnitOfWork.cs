using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure;

/// <summary>EF Core-backed <see cref="IQuestionBankUnitOfWork"/>, wrapping <see cref="QuestionBankDbContext"/>.</summary>
public sealed class QuestionBankUnitOfWork(QuestionBankDbContext context) : IQuestionBankUnitOfWork
{
    /// <summary>Postgres's SQLSTATE for a unique-constraint violation.</summary>
    private const string UniqueViolation = "23505";

    /// <inheritdoc />
    /// <exception cref="ConcurrencyConflictError">
    /// Another request changed the same book, or added a chapter with the same title or position at the same moment;
    /// the caller reloads and tries again.
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
            // Only the unique indexes on a chapter's (book, title) and (book, position) can raise this: two requests
            // adding to the same book at once. An expected race, not a server fault.
            throw new ConcurrencyConflictError(ex);
        }
    }
}
