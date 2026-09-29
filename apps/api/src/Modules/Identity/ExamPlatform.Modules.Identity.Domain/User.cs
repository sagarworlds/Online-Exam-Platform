using ExamPlatform.Modules.Identity.Domain.Events;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>
/// A platform account. The aggregate root for identity, roles, and sessions:
/// role assignment and the "one active session" invariant (FR-4) are both
/// enforced here so they can never be violated by code that only sees part of
/// the picture.
/// </summary>
public sealed class User : AggregateRoot
{
    private readonly List<Role> _roles = [];
    private readonly List<UserSession> _sessions = [];

    /// <summary>Email address, if provided. At least one of email or phone is required.</summary>
    public string? Email { get; private set; }

    /// <summary>Phone number, if provided. At least one of email or phone is required.</summary>
    public string? PhoneNumber { get; private set; }

    /// <summary>Password hash, present only for users who use password + 2FA login (typically staff/admin roles).</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>Date of birth, used to derive <see cref="AgeBand"/> for guardian-consent gating elsewhere in the platform.</summary>
    public DateOnly DateOfBirth { get; private set; }

    /// <summary>Name shown in the UI.</summary>
    public string DisplayName { get; private set; }

    /// <summary>Current lifecycle status.</summary>
    public UserStatus Status { get; private set; }

    /// <summary>Roles currently assigned to this user.</summary>
    public IReadOnlyCollection<Role> Roles => _roles.AsReadOnly();

    /// <summary>This user's login sessions, active and revoked.</summary>
    public IReadOnlyCollection<UserSession> Sessions => _sessions.AsReadOnly();

    private User(
        Guid id,
        string? email,
        string? phoneNumber,
        DateOnly dateOfBirth,
        string displayName) : base(id)
    {
        Email = email;
        PhoneNumber = phoneNumber;
        DateOfBirth = dateOfBirth;
        DisplayName = displayName;
        Status = UserStatus.PendingVerification;
    }

    /// <summary>Registers a new candidate/user account.</summary>
    /// <param name="email">Email address, if provided.</param>
    /// <param name="phoneNumber">Phone number, if provided.</param>
    /// <param name="dateOfBirth">Date of birth.</param>
    /// <param name="displayName">Name to show in the UI.</param>
    /// <param name="nowUtc">The current instant, for the registration event's timestamp.</param>
    /// <exception cref="ArgumentException">Neither an email nor a phone number was supplied.</exception>
    public static User Register(string? email, string? phoneNumber, DateOnly dateOfBirth, string displayName, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ArgumentException("A user must have an email address, a phone number, or both.");
        }

        var user = new User(Guid.NewGuid(), email, phoneNumber, dateOfBirth, displayName);
        user.AddDomainEvent(new UserRegisteredEvent(user.Id, nowUtc));
        return user;
    }

    /// <summary>Sets the password hash, for roles that use password + 2FA login instead of OTP-only login.</summary>
    /// <param name="passwordHash">The hashed password, never the plaintext.</param>
    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    /// <summary>Marks the account verified and active, e.g. after the first successful OTP login.</summary>
    public void Activate() => Status = UserStatus.Active;

    /// <summary>Changes the name shown in the UI.</summary>
    /// <param name="displayName">The new display name.</param>
    public void UpdateDisplayName(string displayName) => DisplayName = displayName;

    /// <summary>
    /// Computes this user's age band as of a given instant. Recomputed on demand
    /// rather than stored, since "is this person a minor" can change as time passes.
    /// </summary>
    /// <param name="asOfUtc">The instant to compute age as of (normally <c>Clock.UtcNow</c>).</param>
    public AgeBand GetAgeBand(DateTime asOfUtc)
    {
        var asOfDate = DateOnly.FromDateTime(asOfUtc);
        var age = asOfDate.Year - DateOfBirth.Year;
        if (DateOfBirth > asOfDate.AddYears(-age)) age--;
        return age < 18 ? AgeBand.Minor : AgeBand.Adult;
    }

    /// <summary>Assigns a role to this user, if not already held.</summary>
    /// <param name="role">The role to grant.</param>
    public void AssignRole(Role role)
    {
        if (!_roles.Contains(role))
        {
            _roles.Add(role);
        }
    }

    /// <summary>Whether any of this user's roles requires completing a second OTP factor at login (FR-3).</summary>
    public bool RequiresTwoFactor => _roles.Any(r => r.RequiresTwoFactor);

    /// <summary>Whether any of this user's roles carries the given permission code.</summary>
    /// <param name="permissionCode">The permission code to check (e.g. "admin.audit.read").</param>
    public bool HasPermission(string permissionCode) => _roles.Any(r => r.HasPermission(permissionCode));

    /// <summary>
    /// Starts a new login session. If another session is already active, it is
    /// revoked as superseded (FR-4: one active session per candidate) unless
    /// <paramref name="allowSupersede"/> is false, in which case the new login
    /// is rejected outright.
    /// </summary>
    /// <param name="sessionTokenHash">Hash of the new session's bearer token.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="expiresAtUtc">When the new session naturally expires.</param>
    /// <param name="deviceFingerprint">Client device fingerprint, if captured.</param>
    /// <param name="ipAddress">Client IP address, if captured.</param>
    /// <param name="allowSupersede">
    /// When true (the default candidate-login behaviour), an existing active session is
    /// revoked to make way for the new one. When false, an existing active session
    /// causes this call to fail instead.
    /// </param>
    /// <exception cref="DuplicateSessionError">
    /// Another session is active and <paramref name="allowSupersede"/> is false.
    /// </exception>
    public UserSession StartNewSession(
        string sessionTokenHash,
        DateTime nowUtc,
        DateTime expiresAtUtc,
        string? deviceFingerprint,
        string? ipAddress,
        bool allowSupersede = true)
    {
        var existingActive = _sessions.FirstOrDefault(s => s.IsActive(nowUtc));

        if (existingActive is not null && !allowSupersede)
        {
            throw new DuplicateSessionError();
        }

        var newSession = new UserSession(Guid.NewGuid(), Id, sessionTokenHash, nowUtc, expiresAtUtc, deviceFingerprint, ipAddress);
        _sessions.Add(newSession);

        if (existingActive is not null)
        {
            existingActive.Revoke(nowUtc, SessionRevocationReason.SupersededByNewLogin);
            AddDomainEvent(new SessionSupersededEvent(Id, existingActive.Id, newSession.Id, nowUtc));
        }

        return newSession;
    }
}
