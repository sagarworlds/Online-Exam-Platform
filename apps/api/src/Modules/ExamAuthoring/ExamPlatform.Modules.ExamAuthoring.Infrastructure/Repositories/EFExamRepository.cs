using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IExamRepository"/>.</summary>
public sealed class EFExamRepository(ExamAuthoringDbContext context) : IExamRepository
{
    /// <inheritdoc />
    public void Add(Exam exam) => context.Exams.Add(exam);

    // Tracked, with children loaded, on purpose: the aggregate's rules read its sections and questions,
    // and handlers rely on change tracking to INSERT new children; an explicit DbSet.Update would flag
    // them Modified instead.
    /// <inheritdoc />
    public async Task<Exam?> GetByIdAsync(Guid examId, CancellationToken cancellationToken = default) =>
        await context.Exams
            .Include(e => e.Sections).ThenInclude(s => s.Questions)
            .Include(e => e.Sections).ThenInclude(s => s.DrawRules)
            .FirstOrDefaultAsync(e => e.Id == examId, cancellationToken);

    /// <inheritdoc />
    public async Task<Exam> GetByIdOrThrowAsync(Guid examId, CancellationToken cancellationToken = default) =>
        await GetByIdAsync(examId, cancellationToken) ?? throw new ExamNotFoundError(examId);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Exam>> ListBySeriesAsync(Guid seriesId, CancellationToken cancellationToken = default) =>
        await context.Exams.AsNoTracking().Where(e => e.SeriesId == seriesId).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Exam>> ListByIdsAsync(IReadOnlyCollection<Guid> examIds, CancellationToken cancellationToken = default) =>
        await context.Exams.AsNoTracking()
            .Include(e => e.Sections).ThenInclude(s => s.Questions)
            .Include(e => e.Sections).ThenInclude(s => s.DrawRules)
            .Where(e => examIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

    // A projection over the question rows, not a load of whole exams: a page of 200 questions can sit in many exams,
    // and only the exam's name and status are wanted. Exams marked deleted are left out by the context's query filter.
    /// <inheritdoc />
    public async Task<IReadOnlyList<ExamQuestionUse>> ListUsesOfQuestionsAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken = default) =>
        await (from exam in context.Exams.AsNoTracking()
               from section in exam.Sections
               from question in section.Questions
               where questionIds.Contains(question.QuestionVersionId)
               select new ExamQuestionUse(question.QuestionVersionId, exam.Id, exam.Name, exam.Status))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Exam>> ListNewestAsync(int take, CancellationToken cancellationToken = default) =>
        await context.Exams.AsNoTracking().OrderByDescending(e => e.CreatedAt).Take(take).ToListAsync(cancellationToken);
}
