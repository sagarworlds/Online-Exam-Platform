using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IIssueReportRepository"/>.</summary>
public sealed class IssueReportRepository(ExamRuntimeDbContext context) : IIssueReportRepository
{
    /// <inheritdoc />
    public void Add(IssueReport report) => context.IssueReports.Add(report);

    /// <inheritdoc />
    public Task<IssueReport?> GetByIdAsync(Guid issueReportId, CancellationToken cancellationToken) =>
        context.IssueReports.FirstOrDefaultAsync(r => r.Id == issueReportId, cancellationToken);

    /// <inheritdoc />
    public Task<int> CountForAttemptAsync(Guid attemptId, CancellationToken cancellationToken) =>
        context.IssueReports.CountAsync(r => r.AttemptId == attemptId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IssueReport>> ListAsync(IssueReportStatus status, int take, CancellationToken cancellationToken) =>
        await context.IssueReports
            .AsNoTracking()
            .Where(r => r.Status == status)
            .OrderBy(r => r.ReportedAtUtc).ThenBy(r => r.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
}
