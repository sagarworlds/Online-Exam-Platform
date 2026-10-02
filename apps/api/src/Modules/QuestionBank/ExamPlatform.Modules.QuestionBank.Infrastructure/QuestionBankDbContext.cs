using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure;

/// <summary>The QuestionBank module's persistence context, scoped to the <c>questionBank</c> Postgres schema.</summary>
public sealed class QuestionBankDbContext(DbContextOptions<QuestionBankDbContext> options) : DbContext(options)
{
    /// <summary>The stored questions.</summary>
    public DbSet<Question> Questions => Set<Question>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("questionBank");

        modelBuilder.Entity<Question>(b =>
        {
            b.ToTable("Questions");
            b.HasKey(q => q.Id);
            // HTML that may embed images, so the unbounded text type; Question.MaxHtmlLength is the real ceiling.
            b.Property(q => q.Text).IsRequired().HasColumnType("text");
            b.Ignore(q => q.DomainEvents);
            b.HasIndex(q => q.CreatedAtUtc);

            b.HasMany(q => q.Options).WithOne().HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(q => q.Options).HasField("_options").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<QuestionOption>(b =>
        {
            b.ToTable("QuestionOptions");
            b.HasKey(o => o.Id);
            b.Property(o => o.Text).IsRequired().HasMaxLength(Question.MaxOptionTextLength);
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
