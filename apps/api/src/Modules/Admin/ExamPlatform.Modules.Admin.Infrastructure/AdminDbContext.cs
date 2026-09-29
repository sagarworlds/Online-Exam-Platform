using System.Text.Json;
using ExamPlatform.Modules.Admin.Domain;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

        // AuditLog is append-only (never updated after insert), so this comparer only
        // needs to satisfy EF's change-tracking snapshot requirement, not support mutation.
        var metadataComparer = new ValueComparer<IReadOnlyDictionary<string, string>>(
            (a, b) => (a ?? new Dictionary<string, string>()).SequenceEqual(b ?? new Dictionary<string, string>()),
            metadata => metadata.Aggregate(0, (hash, kvp) => HashCode.Combine(hash, kvp.Key, kvp.Value)),
            metadata => new Dictionary<string, string>(metadata));

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.ToTable("AuditLogs");
            b.HasKey(a => a.Id);
            b.Property(a => a.Action).IsRequired().HasMaxLength(200);
            b.Property(a => a.EntityType).IsRequired().HasMaxLength(200);
            b.Property(a => a.EntityId).IsRequired().HasMaxLength(200);
            b.Property(a => a.ActorRole).HasMaxLength(100);
            b.Property(a => a.CorrelationId).HasMaxLength(200);
            b.Property(a => a.Metadata).HasConversion(metadataConverter, metadataComparer).HasColumnType("jsonb");
            b.HasIndex(a => new { a.EntityType, a.OccurredAtUtc });
            b.HasIndex(a => a.ActorUserId);
        });

        modelBuilder.ApplyUtcDateTimeConversion();
    }
}
