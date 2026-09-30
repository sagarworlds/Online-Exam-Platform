using Microsoft.EntityFrameworkCore;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure.Repositories;

/// EF Core implementation of IExamRepository.
public class EFExamRepository(ExamAuthoringDbContext context) : IExamRepository
{
    public void Add(Exam exam) => context.Exams.Add(exam);

    // Tracked, with children loaded, on purpose: the aggregate's rules (duplicate/capacity/
    // validity checks) read its child collection, and handlers rely on change tracking to
    // INSERT new children; an explicit DbSet.Update would flag them Modified instead.
    public async Task<Exam?> GetByIdAsync(Guid examId, CancellationToken cancellationToken = default) =>
        await context.Exams.Include(e => e.Sections).ThenInclude(s => s.Questions).FirstOrDefaultAsync(e => e.Id == examId, cancellationToken);

    public async Task<Exam> GetByIdOrThrowAsync(Guid examId, CancellationToken cancellationToken = default)
    {
        var exam = await GetByIdAsync(examId, cancellationToken);
        if (exam == null) throw new InvalidOperationException($"Exam with ID {examId} not found.");
        return exam;
    }

    public async Task<IReadOnlyList<Exam>> ListBySeriesAsync(Guid seriesId, CancellationToken cancellationToken = default) =>
        await context.Exams.AsNoTracking().Where(e => e.SeriesId == seriesId).ToListAsync(cancellationToken);
}
