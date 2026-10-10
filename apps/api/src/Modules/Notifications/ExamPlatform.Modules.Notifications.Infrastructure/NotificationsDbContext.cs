using ExamPlatform.Modules.Notifications.Domain;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Notifications.Infrastructure;

/// <summary>The Notifications module's persistence context, scoped to the <c>notifications</c> Postgres schema.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    /// <summary>The in-app feed's notices.</summary>
    public DbSet<InAppNotification> InAppNotifications => Set<InAppNotification>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");

        modelBuilder.Entity<InAppNotification>(b =>
        {
            b.ToTable("InAppNotifications");
            b.HasKey(n => n.Id);
            b.Property(n => n.Kind).HasConversion<string>().HasMaxLength(50);
            b.Property(n => n.ExamName).HasMaxLength(InAppNotification.MaxExamNameLength);

            // The store's own guard for "one notice per recipient, kind and subject": a check in the application can race another request, and
            // this index is what makes the second insert fail instead of duplicating the notice.
            b.HasIndex(n => new { n.RecipientUserId, n.Kind, n.SubjectId }).IsUnique();

            // The feed reads one account's notices newest first.
            b.HasIndex(n => new { n.RecipientUserId, n.CreatedAtUtc });
            b.Ignore(n => n.IsRead);
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
