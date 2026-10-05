using Microsoft.EntityFrameworkCore;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;
using ExamPlatform.Modules.Batch.Domain;
using ExamPlatform.SharedKernel.Infrastructure;

namespace ExamPlatform.Modules.Batch.Infrastructure;

/// EF Core DbContext for Batch module (exam batches, membership, and roster management).
public class BatchDbContext(DbContextOptions<BatchDbContext> options) : DbContext(options)
{
    /// <summary>Name of the unique index that gives an e-mail address one active seat per batch.</summary>
    public const string ActiveMemberEmailIndexName = "IX_BatchMembers_BatchId_Email";

    public DbSet<BatchAggregate> Batches => Set<BatchAggregate>();
    public DbSet<BatchMember> BatchMembers => Set<BatchMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Batch aggregate
        modelBuilder.Entity<BatchAggregate>(b =>
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
            b.Navigation(x => x.Members).HasField("_members").UsePropertyAccessMode(PropertyAccessMode.Field);

            // Soft delete filter
            b.HasQueryFilter(x => !x.IsDeleted);

            b.ToTable("Batches", "batch");
        });

        // BatchMember entity
        modelBuilder.Entity<BatchMember>(bm =>
        {
            bm.HasKey(x => x.Id);
            bm.Property(x => x.Id).ValueGeneratedNever();

            bm.Property(x => x.BatchId).IsRequired();
            bm.Property(x => x.Email).HasMaxLength(255).IsRequired();
            bm.Property(x => x.Phone).HasMaxLength(20);
            bm.Property(x => x.Status).HasConversion<string>();
            bm.Property(x => x.IsDeleted);

            // The aggregate refuses a second seat for an address, but two requests at once can both pass
            // that check; the index is what stops the second one from being stored.
            bm.HasIndex(x => new { x.BatchId, x.Email }, ActiveMemberEmailIndexName)
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            // Soft delete filter
            bm.HasQueryFilter(x => !x.IsDeleted);

            bm.ToTable("BatchMembers", "batch");
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
