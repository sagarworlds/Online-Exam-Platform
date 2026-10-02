using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>The <see cref="IExamCatalog"/> other modules read exams through.</summary>
public sealed class ExamCatalog(IExamRepository repository) : IExamCatalog
{
    /// <inheritdoc />
    public async Task<ExamSnapshot?> FindAsync(Guid examId, CancellationToken cancellationToken)
    {
        var exams = await repository.ListByIdsAsync([examId], cancellationToken);
        return exams.Select(ToSnapshot).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExamSnapshot>> FindPublishedAsync(IReadOnlyCollection<Guid> examIds, CancellationToken cancellationToken)
    {
        if (examIds.Count == 0)
            return [];

        var exams = await repository.ListByIdsAsync(examIds, cancellationToken);
        return exams.Where(e => e.Status == ExamStatus.Published).Select(ToSnapshot).ToList();
    }

    private static ExamSnapshot ToSnapshot(Exam exam) =>
        new(
            exam.Id,
            exam.Name,
            exam.Description,
            exam.Status == ExamStatus.Published,
            exam.ScheduledStartTime,
            exam.ScheduledEndTime,
            exam.LateEntryDeadline,
            exam.Config.TotalTimeSeconds,
            exam.Config.MarkingScheme.CorrectMarks,
            exam.Config.MarkingScheme.IncorrectMarks,
            exam.Config.MarkingScheme.UnattemptedMarks,
            exam.Sections
                .OrderBy(s => s.Order)
                .Select(s => new ExamSectionSnapshot(
                    s.Id,
                    s.Name,
                    s.Order,
                    s.Questions.OrderBy(q => q.Order).Select(q => q.QuestionVersionId).ToList()))
                .ToList());
}
