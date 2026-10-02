using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.Infrastructure;

/// <summary>The Invite module's persistence context, scoped to the <c>invite</c> Postgres schema.</summary>
public class InviteDbContext(DbContextOptions<InviteDbContext> options) : DbContext(options)
{
    /// <summary>The stored invites.</summary>
    public DbSet<InviteAggregate> Invites => Set<InviteAggregate>();

    /// <summary>The stored invite codes.</summary>
    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<InviteAggregate>(i =>
        {
            i.HasKey(x => x.Id);
            i.Property(x => x.Id).ValueGeneratedNever();
            i.Property(x => x.Email).HasMaxLength(255).IsRequired();
            i.Property(x => x.Status).HasConversion<string>();
            i.Property(x => x.IsDeleted);

            // "Which exams has this user accepted an invite to" is asked for every candidate screen.
            i.HasIndex(x => new { x.AcceptedByUserId, x.ExamId });

            i.HasMany(x => x.Codes)
                .WithOne()
                .HasForeignKey("InviteId")
                .OnDelete(DeleteBehavior.Cascade);

            i.HasQueryFilter(x => !x.IsDeleted);
            i.ToTable("Invites", "invite");
        });

        modelBuilder.Entity<InviteCode>(c =>
        {
            c.HasKey(x => x.Id);
            c.Property(x => x.Id).ValueGeneratedNever();
            c.Property(x => x.InviteId).IsRequired();
            c.Property(x => x.Code).HasMaxLength(8).IsRequired();

            // A code identifies its invite on its own (the link carries nothing else), so two invites
            // can never share one.
            c.HasIndex(x => x.Code).IsUnique();
            c.ToTable("InviteCodes", "invite");
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
