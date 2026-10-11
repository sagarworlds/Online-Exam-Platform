using ExamPlatform.Modules.Proctoring.Domain;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Proctoring.Infrastructure;

/// <summary>The Proctoring module's persistence context, scoped to the <c>proctoring</c> Postgres schema.</summary>
public sealed class ProctoringDbContext(DbContextOptions<ProctoringDbContext> options) : DbContext(options)
{
    /// <summary>The risk assessments of attempts, with their signals.</summary>
    public DbSet<RiskAssessment> RiskAssessments => Set<RiskAssessment>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("proctoring");

        modelBuilder.Entity<RiskAssessment>(a =>
        {
            a.ToTable("RiskAssessments");
            a.HasKey(x => x.Id);
            a.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            a.Property(x => x.DecisionNote).HasMaxLength(RiskAssessment.MaxNoteLength);
            // One assessment per attempt: a second scan that raced the first must not create a duplicate row for the same attempt.
            a.HasIndex(x => x.AttemptId).IsUnique();
            // The review queue reads one exam's flags by status, so that is the index it can seek on.
            a.HasIndex(x => new { x.ExamId, x.Status });

            a.OwnsMany(x => x.Signals, s =>
            {
                s.ToTable("RiskSignalReadings");
                s.WithOwner().HasForeignKey("RiskAssessmentId");
                s.HasKey("RiskAssessmentId", nameof(RiskSignalReading.Kind));
                s.Property(r => r.Kind).HasConversion<string>().HasMaxLength(40);
                s.Property(r => r.RaisedWhen).HasConversion<string>().HasMaxLength(10);
                s.Property(r => r.Value).HasPrecision(18, 4);
                s.Property(r => r.Threshold).HasPrecision(18, 4);
            });
            a.Navigation(x => x.Signals).HasField("_signals").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
