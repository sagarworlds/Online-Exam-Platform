using Microsoft.EntityFrameworkCore;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.SharedKernel.Infrastructure;

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure;

/// EF Core DbContext for ExamAuthoring module.
public class ExamAuthoringDbContext(DbContextOptions<ExamAuthoringDbContext> options) : DbContext(options)
{
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamSection> ExamSections => Set<ExamSection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Exam>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.TimeZone).HasMaxLength(50).IsRequired();
            e.Property(x => x.IsDeleted);

            e.OwnsOne(x => x.Config, c =>
            {
                c.Property(x => x.ResultReleaseMode).HasConversion<string>();

                // MarkingScheme is a three-decimal value object, not a scalar, so it cannot go
                // through a value converter; nesting it as an owned type flattens it into
                // MarkingScheme_* columns on the Exams table.
                c.OwnsOne(x => x.MarkingScheme);
            });

            // What the exam's questions may be drawn from. Flattened into the Exams table; the chapter ids are a Postgres
            // uuid[] because they are ids into another module's schema, where no foreign key can reach.
            e.OwnsOne(x => x.Scope, s =>
            {
                s.Property(x => x.Type).HasConversion<string>().HasColumnName("ScopeType").HasMaxLength(20);
                s.Property(x => x.BookId).HasColumnName("ScopeBookId");
                s.Property(x => x.ChapterIds).HasColumnName("ScopeChapterIds");
            });
            e.Navigation(x => x.Scope).IsRequired();

            e.HasMany(x => x.Sections)
                .WithOne()
                .HasForeignKey("ExamId")
                .OnDelete(DeleteBehavior.Cascade);

            e.HasQueryFilter(x => !x.IsDeleted);
            e.ToTable("Exams", "examAuthoring");
        });

        modelBuilder.Entity<ExamSection>(s =>
        {
            s.HasKey(x => x.Id);
            s.Property(x => x.Id).ValueGeneratedNever();
            s.Property(x => x.ExamId).IsRequired();
            s.Property(x => x.Name).HasMaxLength(255).IsRequired();

            s.HasMany(x => x.Questions)
                .WithOne()
                .HasForeignKey(q => q.SectionId)
                .OnDelete(DeleteBehavior.Cascade);

            s.HasMany(x => x.DrawRules)
                .WithOne()
                .HasForeignKey(r => r.SectionId)
                .OnDelete(DeleteBehavior.Cascade);

            s.ToTable("ExamSections", "examAuthoring");
        });

        // Configured explicitly (rather than left to discovery via ExamSection.Questions) so the
        // table stays in this module's schema instead of falling into the default "public" one.
        modelBuilder.Entity<ExamQuestion>(q =>
        {
            q.HasKey(x => x.Id);
            q.Property(x => x.Id).ValueGeneratedNever();

            // The question bank asks "which exams hold this question?" before it lets a question be deleted or its answers
            // changed. Without this the answer would be a scan of every exam question.
            q.HasIndex(x => x.QuestionVersionId);
            q.ToTable("ExamQuestions", "examAuthoring");
        });

        modelBuilder.Entity<SectionDrawRule>(r =>
        {
            r.HasKey(x => x.Id);
            r.Property(x => x.Id).ValueGeneratedNever();
            r.Property(x => x.Difficulty).HasMaxLength(10);
            r.Property(x => x.Topic).HasMaxLength(SectionDrawRule.MaxTopicLength);
            r.ToTable("SectionDrawRules", "examAuthoring");
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
