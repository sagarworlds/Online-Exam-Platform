using Microsoft.EntityFrameworkCore;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure.Repositories;

/// EF Core implementation of IExamRepository.
public class EFExamRepository(ExamAuthoringDbContext context) : IExamRepository
{
    public void Add(Exam exam) => context.Exams.Add(exam);

    public async Task<Exam?> GetByIdAsync(Guid examId, CancellationToken cancellationToken = default) =>
        await context.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId, cancellationToken);

    public async Task<Exam> GetByIdOrThrowAsync(Guid examId, CancellationToken cancellationToken = default)
    {
        var exam = await GetByIdAsync(examId, cancellationToken);
        if (exam == null) throw new InvalidOperationException($"Exam with ID {examId} not found.");
        return exam;
    }

    public async Task<IReadOnlyList<Exam>> ListBySeriesAsync(Guid seriesId, CancellationToken cancellationToken = default) =>
        await context.Exams.AsNoTracking().Where(e => e.SeriesId == seriesId).ToListAsync(cancellationToken);

    public void Update(Exam exam) => context.Exams.Update(exam);
}
