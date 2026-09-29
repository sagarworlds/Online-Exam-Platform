using System.Text.Json;
using ExamPlatform.Modules.Admin.Domain;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ExamPlatform.Modules.Admin.Infrastructure;

/// <summary>The Admin module's persistence context, scoped to the <c>admin</c> Postgres schema.</summary>
public sealed class AdminDbContext(DbContextOptions<AdminDbContext> options) : DbContext(options)
{
    /// <summary>The audit trail.</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("admin");

        var metadataConverter = new ValueConverter<IReadOnlyDictionary<string, string>, string>(
            metadata => JsonSerializer.Serialize(metadata, (JsonSerializerOptions?)null),
            json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, (JsonSerializerOptions?)null) ?? new());

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.ToTable("AuditLogs");
            b.HasKey(a => a.Id);
            b.Property(a => a.Action).IsRequired().HasMaxLength(200);
            b.Property(a => a.EntityType).IsRequired().HasMaxLength(200);
            b.Property(a => a.EntityId).IsRequired().HasMaxLength(200);
            b.Property(a => a.ActorRole).HasMaxLength(100);
            b.Property(a => a.CorrelationId).HasMaxLength(200);
            b.Property(a => a.Metadata).HasConversion(metadataConverter).HasColumnType("jsonb");
            b.HasIndex(a => new { a.EntityType, a.OccurredAtUtc });
            b.HasIndex(a => a.ActorUserId);
        });

        modelBuilder.ApplyUtcDateTimeConversion();
    }
}
