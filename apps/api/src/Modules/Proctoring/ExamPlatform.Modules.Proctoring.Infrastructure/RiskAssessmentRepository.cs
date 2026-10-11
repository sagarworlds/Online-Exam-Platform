using ExamPlatform.Modules.Proctoring.Application;
using ExamPlatform.Modules.Proctoring.Application.Ports;
using ExamPlatform.Modules.Proctoring.Domain;
using ExamPlatform.Modules.Proctoring.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Proctoring.Infrastructure;

/// <summary>EF Core-backed <see cref="IRiskAssessmentRepository"/>.</summary>
public sealed class RiskAssessmentRepository(ProctoringDbContext context) : IRiskAssessmentRepository
{
    /// <inheritdoc />
    public void Add(RiskAssessment assessment) => context.RiskAssessments.Add(assessment);

    // Tracked on purpose: a scan rescores open assessments and relies on change tracking to save the new readings. The signals are
    // owned by the assessment, so they load with it.
    /// <inheritdoc />
    public async Task<IReadOnlyList<RiskAssessment>> ListForExamAsync(Guid examId, CancellationToken cancellationToken) =>
        await context.RiskAssessments.Where(a => a.ExamId == examId).ToListAsync(cancellationToken);

    // Tracked on purpose: a decision changes the assessment and the unit of work saves it.
    /// <inheritdoc />
    public async Task<RiskAssessment?> GetByIdAsync(Guid assessmentId, CancellationToken cancellationToken) =>
        await context.RiskAssessments.FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken);

    /// <inheritdoc />
    public async Task<RiskFlagPage> ListFlagsAsync(Guid examId, RiskFlagFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var flagged = context.RiskAssessments.AsNoTracking().Where(a => a.ExamId == examId && a.Flagged);
        var matching = filter switch
        {
            RiskFlagFilter.Open => flagged.Where(a => a.Status == RiskFlagStatus.Open),
            RiskFlagFilter.Reviewed => flagged.Where(a => a.Status == RiskFlagStatus.Reviewed),
            RiskFlagFilter.Dismissed => flagged.Where(a => a.Status == RiskFlagStatus.Dismissed),
            _ => flagged,
        };

        var total = await matching.CountAsync(cancellationToken);
        // Ties on score are broken by attempt, so the same row is never on two pages when the scores are equal.
        var items = await matching
            .OrderByDescending(a => a.Score)
            .ThenBy(a => a.AttemptNumber)
            .ThenBy(a => a.AttemptId)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new RiskFlagPage(items, total);
    }

    /// <inheritdoc />
    public Task<bool> HasOpenFlagAsync(Guid attemptId, CancellationToken cancellationToken) =>
        context.RiskAssessments.AsNoTracking()
            .AnyAsync(a => a.AttemptId == attemptId && a.Flagged && a.Status == RiskFlagStatus.Open, cancellationToken);
}

/// <summary>EF Core-backed <see cref="IProctoringUnitOfWork"/>, wrapping <see cref="ProctoringDbContext"/>.</summary>
public sealed class ProctoringUnitOfWork(ProctoringDbContext context) : IProctoringUnitOfWork
{
    /// <inheritdoc />
    /// <exception cref="ScanAlreadyRunningError">Another scan wrote the same attempt's assessment first; this save was refused as a whole.</exception>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (OverlappingScanDetector.IsOverlappingScan(error))
        {
            // Nothing from this save was written: a save is one transaction, so the other scan's rows stand and this scan's are rolled back.
            throw new ScanAlreadyRunningError(error);
        }
    }
}
