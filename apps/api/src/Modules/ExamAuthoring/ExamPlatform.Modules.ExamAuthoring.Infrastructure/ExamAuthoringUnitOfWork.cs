using ExamPlatform.Modules.ExamAuthoring.Application;

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure;

public class ExamAuthoringUnitOfWork(ExamAuthoringDbContext context) : IExamAuthoringUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await context.SaveChangesAsync(cancellationToken);
}
