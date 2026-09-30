using Microsoft.EntityFrameworkCore;
using ExamPlatform.Modules.Batch.Domain;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Infrastructure;

/// EF Core DbContext for Batch module (exam batches, membership, and roster management).
public class BatchDbContext(DbContextOptions<BatchDbContext> options) : DbContext(options)
{
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<BatchMember> BatchMembers => Set<BatchMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Batch aggregate
        modelBuilder.Entity<Batch>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();

            b.Property(x => x.Name).HasMaxLength(255).IsRequired();
            b.Property(x => x.Description).HasMaxLength(1000);
            b.Property(x => x.Status).HasConversion<string>();
            b.Property(x => x.IsDeleted);

            // Members navigation
            b.HasMany(x => x.Members)
                .WithOne()
                .HasForeignKey("BatchId")
                .OnDelete(DeleteBehavior.Cascade);

            // Soft delete filter
            b.HasQueryFilter(x => !x.IsDeleted);

            b.ToTable("Batches", "batch");
        });

        // BatchMember entity
        modelBuilder.Entity<BatchMember>(bm =>
        {
            bm.HasKey(x => x.Id);
            bm.Property(x => x.Id).ValueGeneratedNever();

            bm.Property(x => x.Email).HasMaxLength(255).IsRequired();
            bm.Property(x => x.Phone).HasMaxLength(20);
            bm.Property(x => x.Status).HasConversion<string>();
            bm.Property(x => x.IsDeleted);

            // Soft delete filter
            bm.HasQueryFilter(x => !x.IsDeleted);

            bm.ToTable("BatchMembers", "batch");
        });
    }
}
