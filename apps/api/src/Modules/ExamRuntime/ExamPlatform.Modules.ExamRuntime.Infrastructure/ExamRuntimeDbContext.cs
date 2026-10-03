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

    /// <summary>The extra attempts administrators have granted.</summary>
    public DbSet<ExtraAttemptGrant> ExtraAttemptGrants => Set<ExtraAttemptGrant>();

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

            // The store, not the code, guarantees each attempt number is taken once per candidate per exam: two parallel
            // "start" calls both try for the next number and cannot both succeed, so the loser is reported as a conflict
            // instead of creating a second sitting nobody allowed. The index also serves "all of a candidate's attempts at an exam".
            b.HasIndex(a => new { a.ExamId, a.CandidateId, a.Number }).IsUnique();
            b.HasIndex(a => a.CandidateId);

            // Optimistic concurrency on xmin: two parallel submits of one attempt cannot both score it.
            b.Property<uint>(RowVersionPropertyName).IsRowVersion();

            b.HasMany(a => a.Answers).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(a => a.Answers).HasField("_answers").UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasMany(a => a.Marks).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(a => a.Marks).HasField("_marks").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<AttemptAnswer>(b =>
        {
            b.ToTable("AttemptAnswers");
            b.HasKey(x => x.Id);

            // At most one answer per question: saving again changes the row rather than adding another.
            b.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();

            // The question bank asks "has anyone answered this question?" before it lets the answer key change. The index
            // above starts with the attempt, so it cannot answer that; this one can.
            b.HasIndex(x => x.QuestionId);
        });

        modelBuilder.Entity<AttemptMark>(b =>
        {
            b.ToTable("AttemptMarks");
            b.HasKey(x => x.Id);

            // At most one mark per question: marking twice at once cannot leave two rows, and the loser is a harmless conflict.
            // The index also serves loading an attempt's marks.
            b.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();
        });

        modelBuilder.Entity<ExtraAttemptGrant>(b =>
        {
            b.ToTable("ExtraAttemptGrants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Reason).HasMaxLength(ExtraAttemptGrant.MaxReasonLength);

            // Grants are numbered one after the last per candidate per exam, so two administrators granting at the same
            // moment both ask for the same number and only one succeeds. The index also serves counting a candidate's grants.
            b.HasIndex(x => new { x.ExamId, x.CandidateId, x.Number }).IsUnique();
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
