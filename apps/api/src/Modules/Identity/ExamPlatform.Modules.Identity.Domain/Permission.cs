using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>
/// A granular, checkable capability (e.g. <c>"admin.audit.read"</c>). Reference
/// data seeded at startup; RBAC checks compare a user's effective permission
/// codes (via their roles) against the code an endpoint requires.
/// </summary>
public sealed class Permission : Entity
{
    /// <summary>The stable, dotted permission code checked at authorization time.</summary>
    public string Code { get; private set; }

    /// <summary>Human-readable description shown in admin tooling.</summary>
    public string Description { get; private set; }

    private Permission(Guid id, string code, string description) : base(id)
    {
        Code = code;
        Description = description;
    }

    /// <summary>Creates a new permission.</summary>
    /// <param name="code">The stable, dotted permission code (e.g. "admin.audit.read").</param>
    /// <param name="description">Human-readable description.</param>
    public static Permission Create(string code, string description) =>
        new(Guid.NewGuid(), code, description);
}
