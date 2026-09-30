using Microsoft.EntityFrameworkCore;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;
using ExamPlatform.Modules.Guardian.Domain;

namespace ExamPlatform.Modules.Guardian.Infrastructure;

public class GuardianDbContext(DbContextOptions<GuardianDbContext> options) : DbContext(options)
{
    public DbSet<GuardianAggregate> Guardians => Set<GuardianAggregate>();
    public DbSet<GuardianLink> GuardianLinks => Set<GuardianLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Guardian>(g =>
        {
            g.HasKey(x => x.Id);
            g.Property(x => x.Id).ValueGeneratedNever();
            g.Property(x => x.Email).HasMaxLength(255).IsRequired();
            g.Property(x => x.FullName).HasMaxLength(255).IsRequired();
            g.Property(x => x.Phone).HasMaxLength(20);
            g.Property(x => x.IsDeleted);

            g.HasMany(x => x.CandidateLinks)
                .WithOne()
                .HasForeignKey("GuardianId")
                .OnDelete(DeleteBehavior.Cascade);

            g.HasQueryFilter(x => !x.IsDeleted);
            g.ToTable("Guardians", "guardian");
        });

        modelBuilder.Entity<GuardianLink>(l =>
        {
            l.HasKey(x => x.Id);
            l.Property(x => x.Id).ValueGeneratedNever();
            l.Property(x => x.VerificationToken).HasMaxLength(255).IsRequired();
            l.Property(x => x.Status).HasConversion<string>();
            l.Property(x => x.IsDeleted);

            l.HasQueryFilter(x => !x.IsDeleted);
            l.ToTable("GuardianLinks", "guardian");
        });
    }
}
