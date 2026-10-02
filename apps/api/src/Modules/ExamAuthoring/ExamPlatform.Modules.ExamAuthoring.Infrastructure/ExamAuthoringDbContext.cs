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

            s.ToTable("ExamSections", "examAuthoring");
        });

        // Configured explicitly (rather than left to discovery via ExamSection.Questions) so the
        // table stays in this module's schema instead of falling into the default "public" one.
        modelBuilder.Entity<ExamQuestion>(q =>
        {
            q.HasKey(x => x.Id);
            q.Property(x => x.Id).ValueGeneratedNever();
            q.ToTable("ExamQuestions", "examAuthoring");
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
