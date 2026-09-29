using Microsoft.AspNetCore.Authorization;

namespace ExamPlatform.Modules.Identity.Endpoints.Authorization;

/// <summary>An authorization requirement satisfied by a "perm" claim matching <see cref="PermissionCode"/>.</summary>
/// <param name="PermissionCode">The permission code an endpoint requires (e.g. "admin.audit.read").</param>
public sealed record PermissionRequirement(string PermissionCode) : IAuthorizationRequirement;
