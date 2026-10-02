using ExamPlatform.Modules.QuestionBank.Application;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure;

/// <summary>EF Core-backed <see cref="IQuestionBankUnitOfWork"/>, wrapping <see cref="QuestionBankDbContext"/>.</summary>
public sealed class QuestionBankUnitOfWork(QuestionBankDbContext context) : IQuestionBankUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
