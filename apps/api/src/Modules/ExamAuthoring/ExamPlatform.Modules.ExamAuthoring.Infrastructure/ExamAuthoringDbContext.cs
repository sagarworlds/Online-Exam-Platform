using Microsoft.EntityFrameworkCore;
using ExamPlatform.Modules.ExamAuthoring.Domain;

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
                c.Property(x => x.MarkingScheme).HasConversion<string>();
                c.Property(x => x.ResultReleaseMode).HasConversion<string>();
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
            s.Property(x => x.Name).HasMaxLength(255).IsRequired();

            s.HasMany(x => x.Questions)
                .WithOne()
                .HasForeignKey("SectionId")
                .OnDelete(DeleteBehavior.Cascade);

            s.ToTable("ExamSections", "examAuthoring");
        });
    }
}
