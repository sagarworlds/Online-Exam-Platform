using ExamPlatform.Modules.Consent.Domain;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Consent.Infrastructure;

/// <summary>The Consent module's persistence context, scoped to the <c>consent</c> Postgres schema.</summary>
public sealed class ConsentDbContext(DbContextOptions<ConsentDbContext> options) : DbContext(options)
{
    // Name of the shadow property that maps an incident's Postgres xmin row version (see the Incident mapping).
    private const string IncidentRowVersion = "RowVersion";

    /// <summary>The consent ledger.</summary>
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    /// <summary>Versioned consent notice text references.</summary>
    public DbSet<NoticeVersion> NoticeVersions => Set<NoticeVersion>();

    /// <summary>The incident and breach log, with its escalation due times (FR-52).</summary>
    public DbSet<Incident> Incidents => Set<Incident>();

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

        modelBuilder.Entity<Incident>(b =>
        {
            b.ToTable("Incidents");
            b.HasKey(i => i.Id);
            b.Property(i => i.Description).IsRequired().HasMaxLength(Incident.MaxDescriptionLength);
            b.Property(i => i.AffectedData).IsRequired().HasMaxLength(Incident.MaxAffectedDataLength);
            b.Property(i => i.Category).HasConversion<string>().HasMaxLength(50);
            b.Property(i => i.Status).HasConversion<string>().HasMaxLength(50);

            // The open-incident list filters on status and sorts by due time, so the two are indexed together.
            b.HasIndex(i => new { i.Status, i.EscalationDueAtUtc });

            // Every status change updates this row, so its xmin changes with each one. Two changes racing on one incident
            // therefore cannot both pass the lifecycle check and both save: the second save fails, and the unit of work
            // reports it as a 409.
            b.Property<uint>(IncidentRowVersion).IsRowVersion();

            // A status change always belongs to an incident, so the key is required, not optional.
            b.HasMany(i => i.StatusChanges)
                .WithOne()
                .HasForeignKey("IncidentId")
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            b.Navigation(i => i.StatusChanges).HasField("_statusChanges").UsePropertyAccessMode(PropertyAccessMode.Field);

            b.Ignore(i => i.DomainEvents);
        });

        modelBuilder.Entity<IncidentStatusChange>(b =>
        {
            b.ToTable("IncidentStatusChanges");
            b.HasKey(c => c.Id);
            b.Property(c => c.FromStatus).HasConversion<string>().HasMaxLength(50);
            b.Property(c => c.ToStatus).HasConversion<string>().HasMaxLength(50);
            b.Property(c => c.Note).IsRequired().HasMaxLength(Incident.MaxNoteLength);
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
