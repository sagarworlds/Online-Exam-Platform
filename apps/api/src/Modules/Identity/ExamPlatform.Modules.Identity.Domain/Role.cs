using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>
/// A named collection of <see cref="Permission"/>s (e.g. "SuperAdmin", "Candidate").
/// <see cref="RequiresTwoFactor"/> is data, not a hardcoded role check, so FR-3's
/// mandatory-2FA-for-admins rule extends to a new admin-ish role by flipping a
/// flag rather than changing login code (Open/Closed Principle).
/// </summary>
public sealed class Role : Entity
{
    private readonly List<Permission> _permissions = [];

    /// <summary>The role's unique name (e.g. "SuperAdmin", "ExamAdmin", "Candidate").</summary>
    public string Name { get; private set; }

    /// <summary>Whether logging in as this role requires completing a second OTP factor (FR-3).</summary>
    public bool RequiresTwoFactor { get; private set; }

    /// <summary>The permissions granted to any user holding this role.</summary>
    public IReadOnlyCollection<Permission> Permissions => _permissions.AsReadOnly();

    private Role(Guid id, string name, bool requiresTwoFactor) : base(id)
    {
        Name = name;
        RequiresTwoFactor = requiresTwoFactor;
    }

    /// <summary>Creates a new role.</summary>
    /// <param name="name">The role's unique name.</param>
    /// <param name="requiresTwoFactor">Whether this role must complete 2FA at login.</param>
    public static Role Create(string name, bool requiresTwoFactor) =>
        new(Guid.NewGuid(), name, requiresTwoFactor);

    /// <summary>Grants this role a permission, if it does not already have it.</summary>
    /// <param name="permission">The permission to grant.</param>
    public void Grant(Permission permission)
    {
        if (!_permissions.Contains(permission))
        {
            _permissions.Add(permission);
        }
    }

    /// <summary>Whether this role carries the given permission code.</summary>
    /// <param name="permissionCode">The permission code to check (e.g. "admin.audit.read").</param>
    public bool HasPermission(string permissionCode) =>
        _permissions.Any(p => p.Code == permissionCode);
}
