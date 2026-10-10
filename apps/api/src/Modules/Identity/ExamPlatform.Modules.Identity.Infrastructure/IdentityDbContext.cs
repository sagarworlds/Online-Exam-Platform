using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.DataRequests;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// The Identity module's persistence context, scoped to the <c>identity</c>
/// Postgres schema. Has no <see cref="DbSet{TEntity}"/> for any other module's
/// entities — the module boundary rule ("modules never reach into each other's
/// tables") is therefore a compiler-enforced fact, not a convention.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    /// <summary>Name of the shadow property that maps a concurrency-checked entity's Postgres <c>xmin</c> row version.</summary>
    internal const string RowVersionPropertyName = "RowVersion";

    // Codes are six digits today; the headroom lets the length change without a migration.
    private const int RevealableCodeMaxLength = 12;

    /// <summary>Registered accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>RBAC roles.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>RBAC permissions.</summary>
    public DbSet<Permission> Permissions => Set<Permission>();

    /// <summary>Outstanding and historical OTP challenges.</summary>
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    /// <summary>Outstanding and historical password reset tokens.</summary>
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    /// <summary>Data-principal requests (FR-48): what a candidate asked for, and how staff answered.</summary>
    public DbSet<DataRequest> DataRequests => Set<DataRequest>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasKey(u => u.Id);
            b.Property(u => u.Email).HasMaxLength(User.MaxEmailLength);
            b.Property(u => u.PhoneNumber).HasMaxLength(User.MaxPhoneNumberLength);
            b.Property(u => u.PasswordHash);
            b.Property(u => u.DisplayName).IsRequired().HasMaxLength(User.MaxDisplayNameLength);
            b.Property(u => u.Status).HasConversion<string>().HasMaxLength(30);
            b.HasIndex(u => u.Email).IsUnique().HasFilter("\"Email\" IS NOT NULL");
            b.HasIndex(u => u.PhoneNumber).IsUnique().HasFilter("\"PhoneNumber\" IS NOT NULL");
            b.Ignore(u => u.DomainEvents);

            b.HasMany(u => u.Roles).WithMany().UsingEntity(j => j.ToTable("UserRoles"));
            b.Navigation(u => u.Roles).HasField("_roles").UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasMany(u => u.Sessions).WithOne().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(u => u.Sessions).HasField("_sessions").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<UserSession>(b =>
        {
            b.ToTable("UserSessions");
            b.HasKey(s => s.Id);
            b.Property(s => s.SessionTokenHash).IsRequired();
            b.Property(s => s.RevokedReason).HasConversion<string>().HasMaxLength(30);
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("Roles");
            b.HasKey(r => r.Id);
            b.Property(r => r.Name).IsRequired().HasMaxLength(100);
            b.HasIndex(r => r.Name).IsUnique();

            b.HasMany(r => r.Permissions).WithMany().UsingEntity(j => j.ToTable("RolePermissions"));
            b.Navigation(r => r.Permissions).HasField("_permissions").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Permission>(b =>
        {
            b.ToTable("Permissions");
            b.HasKey(p => p.Id);
            b.Property(p => p.Code).IsRequired().HasMaxLength(150);
            b.Property(p => p.Description).HasMaxLength(500);
            b.HasIndex(p => p.Code).IsUnique();
        });

        modelBuilder.Entity<DataRequest>(b =>
        {
            b.ToTable("DataRequests");
            b.HasKey(r => r.Id);
            b.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.Details).HasMaxLength(DataRequest.MaxDetailsLength);
            b.Property(r => r.ResolutionNote).HasMaxLength(DataRequest.MaxNoteLength);
            b.Ignore(r => r.DomainEvents);

            // The lookups staff and the candidate make: one account's requests, and the open queue in due order.
            b.HasIndex(r => new { r.UserId, r.Kind, r.Status });
            b.HasIndex(r => new { r.Status, r.DueAtUtc });
        });

        modelBuilder.Entity<OtpChallenge>(b =>
        {
            b.ToTable("OtpChallenges");
            b.HasKey(c => c.Id);
            b.Property(c => c.Destination).IsRequired().HasMaxLength(OtpChallenge.MaxDestinationLength);
            b.Property(c => c.CodeHash).IsRequired();
            b.Property(c => c.RevealableCode).HasMaxLength(RevealableCodeMaxLength);
            b.Property(c => c.Channel).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.Purpose).HasConversion<string>().HasMaxLength(30);
            b.Ignore(c => c.DomainEvents);

            // Every OTP issue looks up the destination's outstanding challenges for the
            // same purpose to supersede them, so that lookup must not scan the table.
            b.HasIndex(c => new { c.Destination, c.Purpose });

            // Optimistic concurrency on Postgres's xmin system column (a shadow uint that
            // Npgsql maps to xmin when marked as a row version), so two parallel verifies
            // of one challenge can neither both consume it nor overwrite each other's
            // attempt count: the second save fails and IdentityUnitOfWork reports it as a 409.
            b.Property<uint>(RowVersionPropertyName).IsRowVersion();
        });

        modelBuilder.Entity<PasswordResetToken>(b =>
        {
            b.ToTable("PasswordResetTokens");
            b.HasKey(t => t.Id);
            b.Property(t => t.TokenHash).IsRequired();
            b.Ignore(t => t.DomainEvents);

            // Every reset request and every completed reset looks up the user's outstanding
            // tokens to revoke them, so that lookup must not scan the table.
            b.HasIndex(t => t.UserId);

            // The same xmin row version as OtpChallenges: two parallel resets with one link
            // cannot both consume it, and a reset racing a newer request cannot overwrite the
            // revocation; the second save fails and IdentityUnitOfWork reports it as a 409.
            b.Property<uint>(RowVersionPropertyName).IsRowVersion();
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
