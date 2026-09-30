using Microsoft.EntityFrameworkCore;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;
using ExamPlatform.Modules.Invite.Domain;

namespace ExamPlatform.Modules.Invite.Infrastructure;

public class InviteDbContext(DbContextOptions<InviteDbContext> options) : DbContext(options)
{
    public DbSet<InviteAggregate> Invites => Set<InviteAggregate>();
    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();

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
            c.Property(x => x.Code).HasMaxLength(8).IsRequired();
            c.ToTable("InviteCodes", "invite");
        });
    }
}
