namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// Settings for the development-only first administrator (<c>Identity:Bootstrap</c>). Staff sign in
/// with a password and a second factor (FR-3) and nothing else creates one, so without this a fresh
/// development database has no way into the admin features. Never honoured outside Development.
/// </summary>
/// <remarks>
/// Keep the password out of source control: set it with
/// <c>dotnet user-secrets set "Identity:Bootstrap:AdminPassword" "..."</c> against the Host project, or
/// with the <c>Identity__Bootstrap__AdminPassword</c> environment variable.
/// </remarks>
public sealed class IdentityBootstrapOptions
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Identity:Bootstrap";

    /// <summary>The administrator's email address, also the login name and the address the second-factor code is sent to.</summary>
    public string? AdminEmail { get; set; }

    /// <summary>The administrator's initial password. Must satisfy the platform's password policy.</summary>
    public string? AdminPassword { get; set; }

    /// <summary>The name shown in the UI for the administrator.</summary>
    public string AdminDisplayName { get; set; } = "Bootstrap Admin";

    /// <summary>Whether any bootstrap setting has been supplied, i.e. the developer asked for an administrator.</summary>
    public bool IsRequested =>
        !string.IsNullOrWhiteSpace(AdminEmail) || !string.IsNullOrWhiteSpace(AdminPassword);
}
