using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Analytics.Infrastructure;

/// <summary>
/// The Analytics module's persistence context, scoped to the <c>analytics</c> Postgres schema. Every module owns one (ADR 0001), so the
/// module's data is reachable only through its own code. It holds no tables yet: candidate analytics are worked out from other modules'
/// released results and stored nowhere, so the first table is the record of report exports (FR-38), added with that feature.
/// </summary>
public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("analytics");
        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
