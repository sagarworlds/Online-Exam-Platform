using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>What <see cref="IdentityBootstrapSeeder.SeedAdminAsync"/> did.</summary>
public enum BootstrapAdminOutcome
{
    /// <summary>No bootstrap setting was supplied, so nothing was done.</summary>
    NotConfigured,

    /// <summary>The administrator account did not exist and was created.</summary>
    Created,

    /// <summary>An account with that email already exists; it was left exactly as it was.</summary>
    AlreadyExists,
}

/// <summary>
/// Creates the development-only first administrator from <see cref="IdentityBootstrapOptions"/>.
/// It knows nothing about environments: the caller (the Identity module installer) only calls it
/// in Development, so a production database can never get an administrator from configuration.
/// </summary>
public static class IdentityBootstrapSeeder
{
    /// <summary>
    /// The date of birth stored for the administrator. The account model requires one (it drives the
    /// age band used for guardian consent) but a staff account has no meaningful value, so this is a
    /// fixed adult placeholder.
    /// </summary>
    public static readonly DateOnly PlaceholderDateOfBirth = new(1980, 1, 1);

    /// <summary>
    /// Creates an active SuperAdmin with the configured email and password, unless an account with
    /// that email already exists. Call it after <see cref="IdentitySeeder.SeedAsync"/> has created the roles.
    /// </summary>
    /// <remarks>
    /// Idempotent and non-destructive: an existing account is never changed, so a developer who
    /// has since changed the password or the roles of the bootstrap account keeps those changes
    /// across restarts. The password is hashed only when the account is actually created.
    /// </remarks>
    /// <param name="context">The Identity module's database context.</param>
    /// <param name="options">The bootstrap settings.</param>
    /// <param name="passwordHasher">Hashes the password; the plaintext is never stored.</param>
    /// <param name="passwordPolicy">Refuses a password that is too weak to protect an administrator.</param>
    /// <param name="nowUtc">The current instant, for the account's registration time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was done.</returns>
    /// <exception cref="InvalidOperationException">
    /// Only one of the email and the password is configured, or the SuperAdmin role has not been seeded.
    /// </exception>
    /// <exception cref="WeakPasswordError">The password does not meet the password policy.</exception>
    public static async Task<BootstrapAdminOutcome> SeedAdminAsync(
        IdentityDbContext context,
        IdentityBootstrapOptions options,
        IPasswordHasher passwordHasher,
        IPasswordPolicy passwordPolicy,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (!options.IsRequested)
        {
            return BootstrapAdminOutcome.NotConfigured;
        }

        // Half a configuration is almost certainly a mistake (a forgotten secret); say so at
        // startup instead of silently creating no administrator, or one nobody can sign in as.
        if (string.IsNullOrWhiteSpace(options.AdminEmail) || string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            throw new InvalidOperationException(
                $"{IdentityBootstrapOptions.SectionName}:AdminEmail and {IdentityBootstrapOptions.SectionName}:AdminPassword " +
                "must be set together, or both left unset.");
        }

        var email = options.AdminEmail.Trim();
        passwordPolicy.EnsureAcceptable(options.AdminPassword, email);

        if (await context.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return BootstrapAdminOutcome.AlreadyExists;
        }

        var superAdmin = await context.Roles
            .Include(r => r.Permissions)
            .SingleOrDefaultAsync(r => r.Name == RbacCatalog.RoleNames.SuperAdmin, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The {RbacCatalog.RoleNames.SuperAdmin} role is not seeded; run {nameof(IdentitySeeder)} first.");

        var admin = User.Register(email, phoneNumber: null, PlaceholderDateOfBirth, options.AdminDisplayName, nowUtc);
        admin.AssignRole(superAdmin);
        admin.SetPasswordHash(passwordHasher.Hash(options.AdminPassword));
        admin.Activate();

        context.Users.Add(admin);
        await context.SaveChangesAsync(cancellationToken);
        return BootstrapAdminOutcome.Created;
    }
}
