using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IQuestionRepository"/>.</summary>
public sealed class QuestionRepository(QuestionBankDbContext context) : IQuestionRepository
{
    /// <inheritdoc />
    public void Add(Question question) => context.Questions.Add(question);

    /// <inheritdoc />
    public Task<Question?> GetByIdAsync(Guid questionId, CancellationToken cancellationToken) =>
        context.Questions.Include(q => q.Options).FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Question>> GetManyAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken) =>
        await context.Questions.AsNoTracking().Include(q => q.Options)
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Question>> ListNewestAsync(int take, CancellationToken cancellationToken) =>
        await context.Questions.AsNoTracking().Include(q => q.Options)
            .OrderByDescending(q => q.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
}
