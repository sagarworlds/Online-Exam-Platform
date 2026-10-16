using System.Net.Mail;
using System.Security.Cryptography;
using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Creates the first administrator of a deployed environment.</summary>
/// <param name="Email">The administrator's e-mail address, exactly as they will sign in with it. Sign-in matches an address exactly.</param>
public sealed record ProvisionFirstAdministratorCommand(string? Email);

/// <summary>
/// Handles <see cref="ProvisionFirstAdministratorCommand"/> (ADR 0005). Staff sign in with a password and a second factor
/// (FR-3) and nothing else creates a staff account, so a new deployed environment has no way in. This is that way, built so
/// that no secret ever has to be written down:
/// <list type="bullet">
/// <item>The account gets a random password that nobody sees, stores or logs. The person sets their own through the
/// ordinary password-reset link, which this handler sends to the address, so only whoever controls that mailbox can sign in.</item>
/// <item>It works only while no Super Admin exists, so it cannot be used later to add an administrator.</item>
/// <item>It never changes an account that already exists.</item>
/// </list>
/// </summary>
public sealed class ProvisionFirstAdministratorHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IPasswordHasher passwordHasher,
    RequestPasswordResetHandler passwordSetup,
    IAuditLogger auditLogger,
    IIdentityUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>The name shown for the administrator until they change it.</summary>
    public const string DisplayName = "Administrator";

    /// <summary>
    /// The date of birth stored for the administrator. The account model requires one (it drives the age band used for
    /// guardian consent) but a staff account has no meaningful value, so this is a fixed adult placeholder.
    /// </summary>
    public static readonly DateOnly PlaceholderDateOfBirth = new(1980, 1, 1);

    /// <summary>
    /// Creates an active Super Admin, records it in the audit trail, and e-mails the address a link to set a password.
    /// </summary>
    /// <param name="command">The administrator's address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new account's id.</returns>
    /// <exception cref="InvalidAdministratorEmailError">The address is not one plain e-mail address.</exception>
    /// <exception cref="RoleNotFoundError">The Super Admin role has not been seeded; the database needs <c>--migrate-and-seed</c> first.</exception>
    /// <exception cref="FirstAdministratorAlreadyExistsError">A Super Admin account already exists.</exception>
    /// <exception cref="DuplicateAccountError">Some other account already uses the address; it is left as it is.</exception>
    public async Task<Guid> HandleAsync(ProvisionFirstAdministratorCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email?.Trim() ?? string.Empty;
        if (!IsPlainAddress(email))
        {
            throw new InvalidAdministratorEmailError();
        }

        var superAdmin = await roleRepository.GetByNameAsync(RbacCatalog.RoleNames.SuperAdmin, cancellationToken)
            ?? throw new RoleNotFoundError();

        if (await userRepository.AnyInRoleAsync(superAdmin.Name, cancellationToken))
        {
            throw new FirstAdministratorAlreadyExistsError();
        }

        // Promoting an existing account is a different act, done by an administrator through the role-assignment
        // route. Doing it here would let this job turn any candidate who knows the address into a Super Admin.
        if (await userRepository.GetByEmailAsync(email, cancellationToken) is not null)
        {
            throw new DuplicateAccountError();
        }

        var admin = User.Register(email, phoneNumber: null, PlaceholderDateOfBirth, DisplayName, clock.UtcNow);
        admin.AssignRole(superAdmin);
        admin.SetPasswordHash(passwordHasher.Hash(UnguessablePassword()));
        admin.Activate();
        await userRepository.AddAsync(admin, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Named by id only, never by address (NFR-6). No actor: nobody was signed in, an operator ran a job.
        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: null,
                ActorRole: null,
                Action: "Identity.FirstAdministratorProvisioned",
                EntityType: "User",
                EntityId: admin.Id.ToString(),
                Metadata: new Dictionary<string, string>(),
                CorrelationId: null),
            cancellationToken);

        // The same link "Forgot password" sends. The account has a password hash, so the existing flow accepts it
        // unchanged; the rule that refuses accounts without a password (FR-1) is left exactly as it is.
        await passwordSetup.HandleAsync(new RequestPasswordResetCommand(email), cancellationToken);

        return admin.Id;
    }

    // Why this is not a policy-checked password: nobody types it. It exists so the account has a hash the reset flow
    // accepts, and 48 random bytes cannot be guessed. It is gone the moment the first reset replaces the hash.
    private static string UnguessablePassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    // TryCreate also accepts "Name <a@b.com>"; comparing the parsed address with the input rejects that.
    private static bool IsPlainAddress(string email) =>
        email.Length is > 0 and <= User.MaxEmailLength
        && MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email;
}
