using ExamPlatform.Modules.Analytics.Application;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Analytics.Infrastructure;

/// <summary>
/// EF Core-backed <see cref="IAnalyticsUnitOfWork"/>, wrapping <see cref="AnalyticsDbContext"/>. A failed save is not swallowed: it is
/// rethrown so the request that asked for it fails, and no report is handed out that was not recorded (FR-38).
/// </summary>
public sealed class AnalyticsUnitOfWork(AnalyticsDbContext context) : IAnalyticsUnitOfWork
{
    /// <inheritdoc />
    /// <exception cref="DbUpdateException">The database refused the save; the caller's request fails with it.</exception>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
