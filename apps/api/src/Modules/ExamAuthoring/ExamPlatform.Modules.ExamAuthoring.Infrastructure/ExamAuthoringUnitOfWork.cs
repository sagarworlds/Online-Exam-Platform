using ExamPlatform.Modules.ExamAuthoring.Application;

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure;

/// <summary>EF Core-backed <see cref="IExamAuthoringUnitOfWork"/>, wrapping <see cref="ExamAuthoringDbContext"/>.</summary>
public sealed class ExamAuthoringUnitOfWork(ExamAuthoringDbContext context) : IExamAuthoringUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
