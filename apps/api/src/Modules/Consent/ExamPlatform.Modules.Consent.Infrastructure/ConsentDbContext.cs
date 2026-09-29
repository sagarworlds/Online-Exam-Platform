using ExamPlatform.Modules.Consent.Domain;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Consent.Infrastructure;

/// <summary>The Consent module's persistence context, scoped to the <c>consent</c> Postgres schema.</summary>
public sealed class ConsentDbContext(DbContextOptions<ConsentDbContext> options) : DbContext(options)
{
    /// <summary>The consent ledger.</summary>
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    /// <summary>Versioned consent notice text references.</summary>
    public DbSet<NoticeVersion> NoticeVersions => Set<NoticeVersion>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("consent");

        modelBuilder.Entity<ConsentRecord>(b =>
        {
            b.ToTable("ConsentRecords");
            b.HasKey(r => r.Id);
            b.Property(r => r.Purpose).HasConversion<string>().HasMaxLength(50);
            b.HasIndex(r => new { r.SubjectId, r.Purpose });
            b.Ignore(r => r.DomainEvents);
        });

        modelBuilder.Entity<NoticeVersion>(b =>
        {
            b.ToTable("NoticeVersions");
            b.HasKey(n => n.Id);
            b.Property(n => n.Purpose).HasConversion<string>().HasMaxLength(50);
            b.Property(n => n.VersionLabel).IsRequired().HasMaxLength(50);
            b.Property(n => n.ContentReference).IsRequired().HasMaxLength(500);
        });

        modelBuilder.ApplyUtcDateTimeConversion();
    }
}
