using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure;

/// <summary>The ExamRuntime module's persistence context, scoped to the <c>examRuntime</c> Postgres schema.</summary>
public sealed class ExamRuntimeDbContext(DbContextOptions<ExamRuntimeDbContext> options) : DbContext(options)
{
    /// <summary>Name of the shadow property that maps an attempt's Postgres <c>xmin</c> row version.</summary>
    internal const string RowVersionPropertyName = "RowVersion";

    /// <summary>The candidates' attempts.</summary>
    public DbSet<Attempt> Attempts => Set<Attempt>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("examRuntime");

        modelBuilder.Entity<Attempt>(b =>
        {
            b.ToTable("Attempts");
            b.HasKey(a => a.Id);
            b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.Score).HasPrecision(10, 3);
            b.Property(a => a.MaxScore).HasPrecision(10, 3);
            b.Ignore(a => a.DomainEvents);

            // The store, not the code, guarantees one attempt per candidate per exam: two parallel "start"
            // calls cannot both succeed, so the loser is reported as a conflict instead of creating a second sitting.
            b.HasIndex(a => new { a.ExamId, a.CandidateId }).IsUnique();
            b.HasIndex(a => a.CandidateId);

            // Optimistic concurrency on xmin: two parallel submits of one attempt cannot both score it.
            b.Property<uint>(RowVersionPropertyName).IsRowVersion();

            b.HasMany(a => a.Answers).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(a => a.Answers).HasField("_answers").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<AttemptAnswer>(b =>
        {
            b.ToTable("AttemptAnswers");
            b.HasKey(x => x.Id);

            // At most one answer per question: saving again changes the row rather than adding another.
            b.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
